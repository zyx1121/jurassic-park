using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The books for every physical unit of goods. Goods are created only by gathering from a node and destroyed only by an
    /// explicit consume; everything else is a transfer that checks the source amount, the destination room and the
    /// reservations together and changes both sides or neither. Register it as a system after the task system: it expires
    /// reservations and drops the goods of holders that died this tick.
    /// </summary>
    public sealed class Logistics : ISimSystem
    {
        private readonly World world;
        private readonly LogisticsConfig config;
        private readonly Dictionary<EntityId, Container> containers = new Dictionary<EntityId, Container>();
        private readonly Dictionary<EntityId, ResourceNode> nodes = new Dictionary<EntityId, ResourceNode>();
        private readonly List<EntityId> holderOrder = new List<EntityId>();
        private readonly Dictionary<EntityId, Entity> holderEntities = new Dictionary<EntityId, Entity>();
        private readonly Dictionary<ReservationId, Reservation> reservations = new Dictionary<ReservationId, Reservation>();
        private readonly List<ReservationId> reservationOrder = new List<ReservationId>();
        private long nextReservationId = 1;
        private readonly SortedDictionary<string, long> consumed = new SortedDictionary<string, long>(StringComparer.Ordinal);

        public Logistics(World world, LogisticsConfig config)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public Container AddContainer(Entity holder, int capacity)
        {
            if (holder == null) throw new ArgumentNullException(nameof(holder));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (holderEntities.ContainsKey(holder.Id)) throw new ArgumentException($"{holder.Id} already holds goods.", nameof(holder));
            var container = new Container(holder.Id, capacity);
            containers.Add(holder.Id, container);
            holderOrder.Add(holder.Id);
            holderEntities.Add(holder.Id, holder);
            return container;
        }

        public ResourceNode AddNode(Entity holder, string resource, int amount)
        {
            if (holder == null) throw new ArgumentNullException(nameof(holder));
            if (string.IsNullOrEmpty(resource)) throw new ArgumentException("A node needs a resource.", nameof(resource));
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (holderEntities.ContainsKey(holder.Id)) throw new ArgumentException($"{holder.Id} already holds goods.", nameof(holder));
            var node = new ResourceNode(holder.Id, resource, amount);
            nodes.Add(holder.Id, node);
            holderOrder.Add(holder.Id);
            holderEntities.Add(holder.Id, holder);
            return node;
        }

        /// <summary>Gives the entity what its definition says it has: a pack or store, or node stock. Call once, right after spawning it.</summary>
        public void Attach(Entity entity, EntityDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.NodeResource != null) AddNode(entity, definition.NodeResource, definition.NodeAmount);
            else if (definition.StorageCapacity > 0) AddContainer(entity, definition.StorageCapacity);
        }

        public bool TryGetContainer(EntityId holder, out Container container) => containers.TryGetValue(holder, out container);

        /// <summary>
        /// Ends a container on a living holder. Whatever it still holds falls to the ground as a pile, so this is never a way to
        /// make goods disappear; consume them first if they are meant to be gone. Releases any reservation on it.
        /// </summary>
        public bool RemoveContainer(EntityId holder)
        {
            if (!containers.TryGetValue(holder, out Container container)) return false;
            for (int i = reservationOrder.Count - 1; i >= 0; i--)
            {
                Reservation r = reservations[reservationOrder[i]];
                if (r.Holder == holder) Release(r);
            }
            if (container.Total > 0) Drop(world, holder, container);
            containers.Remove(holder);
            holderOrder.Remove(holder);
            holderEntities.Remove(holder);
            return true;
        }
        public bool TryGetNode(EntityId holder, out ResourceNode node) => nodes.TryGetValue(holder, out node);

        /// <summary>Room in the container already promised to deposits, by anyone. With a resource, only the promises made for that resource.</summary>
        public int ReservedRoomIn(EntityId holder, string resource = null)
        {
            if (resource == null) return containers.TryGetValue(holder, out Container container) ? container.ReservedForDeposit : 0;
            int total = 0;
            for (int i = 0; i < reservationOrder.Count; i++)
            {
                Reservation r = reservations[reservationOrder[i]];
                if (r.Kind == ReservationKind.Deposit && r.Holder == holder && r.Resource == resource) total += r.Amount;
            }
            return total;
        }

        /// <summary>Goods in the container that no withdrawal reservation other than <paramref name="onBehalfOf"/>'s has claimed.</summary>
        public int AvailableIn(EntityId holder, string resource, TaskId onBehalfOf = default)
        {
            if (!containers.TryGetValue(holder, out Container container)) return 0;
            int reservedByOthers = 0;
            for (int i = 0; i < reservationOrder.Count; i++)
            {
                Reservation r = reservations[reservationOrder[i]];
                if (r.Kind == ReservationKind.Withdrawal && r.Holder == holder && r.Owner != onBehalfOf && r.Resource == resource) reservedByOthers += r.Amount;
            }
            return Math.Max(0, container.AmountOf(resource) - reservedByOthers);
        }

        // ---- reservations ----

        /// <summary>Promises up to <paramref name="amount"/> and returns what was actually promised, possibly zero. Never promises more than exists.</summary>
        public Reservation Reserve(ReservationKind kind, EntityId holder, string resource, int amount, TaskId owner)
        {
            if (amount < 1 || owner.IsNone) return null;
            int granted;
            switch (kind)
            {
                case ReservationKind.NodeStock:
                    if (!nodes.TryGetValue(holder, out ResourceNode node) || node.Resource != resource) return null;
                    granted = Math.Min(amount, node.Unreserved);
                    if (granted < 1) return null;
                    node.Reserved += granted;
                    break;
                case ReservationKind.Deposit:
                    if (!containers.TryGetValue(holder, out Container into)) return null;
                    granted = Math.Min(amount, into.FreeCapacity);
                    if (granted < 1) return null;
                    into.ReservedForDeposit += granted;
                    break;
                default:
                    granted = Math.Min(amount, AvailableIn(holder, resource));
                    if (granted < 1) return null;
                    break;
            }
            var reservation = new Reservation(new ReservationId(nextReservationId++), kind, holder, resource, granted, owner,
                world.Tick + config.ReservationLifetimeTicks);
            reservations.Add(reservation.Id, reservation);
            reservationOrder.Add(reservation.Id);
            return reservation;
        }

        /// <summary>Keeps a live task's reservation from expiring.</summary>
        public void Renew(Reservation reservation)
        {
            if (reservation != null && reservations.ContainsKey(reservation.Id)) reservation.ExpiresAtTick = world.Tick + config.ReservationLifetimeTicks;
        }

        /// <summary>Gives the promise back. Safe to call again, on null, or after expiry: it never refunds twice.</summary>
        public void Release(Reservation reservation)
        {
            if (reservation == null || !reservations.Remove(reservation.Id)) return;
            reservationOrder.Remove(reservation.Id);
            Unbook(reservation, reservation.Amount);
            reservation.Amount = 0;
        }

        public bool IsLive(Reservation reservation) => reservation != null && reservations.ContainsKey(reservation.Id);

        private void Unbook(Reservation reservation, int amount)
        {
            if (reservation.Kind == ReservationKind.NodeStock && nodes.TryGetValue(reservation.Holder, out ResourceNode node)) node.Reserved -= amount;
            else if (reservation.Kind == ReservationKind.Deposit && containers.TryGetValue(reservation.Holder, out Container into)) into.ReservedForDeposit -= amount;
        }

        /// <summary>Uses up part of a reservation as the promised goods actually move.</summary>
        private void Settle(Reservation reservation, int amount)
        {
            if (!IsLive(reservation)) return;
            int used = Math.Min(amount, reservation.Amount);
            Unbook(reservation, used);
            reservation.Amount -= used;
            if (reservation.Amount == 0)
            {
                reservations.Remove(reservation.Id);
                reservationOrder.Remove(reservation.Id);
            }
        }

        /// <summary>Setup only: puts goods into a container as the scenario's starting stock, before the first tick. Never a way to make goods during a match.</summary>
        public int Seed(EntityId holder, string resource, int amount)
        {
            if (world.Tick != 0) throw new InvalidOperationException("Starting stock is placed before the match begins.");
            if (amount < 1 || string.IsNullOrEmpty(resource) || !containers.TryGetValue(holder, out Container container)) return 0;
            int placed = Math.Min(amount, container.FreeCapacity);
            if (placed < 1) return 0;
            container.Add(resource, placed);
            return placed;
        }

        // ---- movements of goods ----

        /// <summary>
        /// Turns node stock into goods in the gatherer's container. Both reservations are optional but, when given, must be live
        /// and are settled by the amount moved. Returns the amount gathered, zero if any side cannot take part.
        /// </summary>
        public int Gather(EntityId nodeHolder, EntityId into, int amount, Reservation stock, Reservation room)
        {
            if (amount < 1 || !nodes.TryGetValue(nodeHolder, out ResourceNode node) || !containers.TryGetValue(into, out Container target)) return 0;
            if (!world.IsAlive(nodeHolder) || !world.IsAlive(into)) return 0;
            // A reservation only counts here if it is for this very node or container; anyone else's promise is ignored, not spent.
            bool ownsStock = IsLive(stock) && stock.Kind == ReservationKind.NodeStock && stock.Holder == nodeHolder;
            bool ownsRoom = IsLive(room) && room.Kind == ReservationKind.Deposit && room.Holder == into;
            int fromNode = ownsStock ? Math.Min(stock.Amount, node.Remaining) : node.Unreserved;
            int moved = (int)Math.Min(amount, Math.Min(fromNode, RoomIn(target, ownsRoom ? room : null)));
            if (moved < 1) return 0;

            if (ownsStock) Settle(stock, moved);
            if (ownsRoom) Settle(room, moved);
            node.Remaining -= moved;
            target.Add(node.Resource, moved);
            world.Raise(new GoodsTransferred(EntityId.None, into, node.Resource, moved));
            if (node.Remaining == 0) world.Raise(new ResourceNodeDepleted(nodeHolder));
            return moved;
        }

        /// <summary>Moves goods between two containers, all checks first, both sides or neither. Returns the amount moved.</summary>
        public int Transfer(EntityId from, EntityId to, string resource, int amount, Reservation withdrawal, Reservation deposit, TaskId onBehalfOf = default)
        {
            if (amount < 1 || from == to) return 0;
            if (!containers.TryGetValue(from, out Container source) || !containers.TryGetValue(to, out Container target)) return 0;
            if (!world.IsAlive(from) || !world.IsAlive(to)) return 0;
            // What this task may take: everything not promised to someone else. Its own reservation is part of that.
            // A reservation is honoured only when it is this task's, for this container and resource. Holding someone else's
            // reservation object must never let a caller take their goods or their room.
            bool ownsWithdrawal = IsLive(withdrawal) && withdrawal.Kind == ReservationKind.Withdrawal && withdrawal.Holder == from
                && withdrawal.Resource == resource && withdrawal.Owner == onBehalfOf;
            bool ownsDeposit = IsLive(deposit) && deposit.Kind == ReservationKind.Deposit && deposit.Holder == to && deposit.Owner == onBehalfOf;
            int takeable = AvailableIn(from, resource, onBehalfOf);
            int moved = (int)Math.Min(amount, Math.Min(takeable, RoomIn(target, ownsDeposit ? deposit : null)));
            if (moved < 1) return 0;

            if (ownsWithdrawal) Settle(withdrawal, moved);
            if (ownsDeposit) Settle(deposit, moved);
            source.Remove(resource, moved);
            target.Add(resource, moved);
            world.Raise(new GoodsTransferred(from, to, resource, moved));
            return moved;
        }

        /// <summary>Room the caller may fill: what is free plus its own promised room. In long, because a ground pile's room is int.MaxValue.</summary>
        private static long RoomIn(Container target, Reservation ownDeposit) =>
            (long)Math.Max(0, target.FreeCapacity) + (ownDeposit != null ? ownDeposit.Amount : 0);

        /// <summary>
        /// Destroys goods on purpose, for example materials built into a wall. Counted, so the conservation check still balances.
        /// Goods promised to another task's withdrawal are not touched.
        /// </summary>
        public int Consume(EntityId from, string resource, int amount, TaskId onBehalfOf = default)
        {
            if (amount < 1 || !containers.TryGetValue(from, out _)) return 0;
            Container source = containers[from];
            int used = Math.Min(amount, AvailableIn(from, resource, onBehalfOf));
            if (used < 1) return 0;
            source.Remove(resource, used);
            Count(resource, used);
            return used;
        }

        /// <summary>Everything of one resource that exists anywhere: node stock plus goods in containers. With <see cref="ConsumedOf"/> it is constant for a match.</summary>
        public long TotalOf(string resource)
        {
            long total = 0;
            for (int i = 0; i < holderOrder.Count; i++)
            {
                if (containers.TryGetValue(holderOrder[i], out Container c)) total += c.AmountOf(resource);
                else if (nodes.TryGetValue(holderOrder[i], out ResourceNode n) && n.Resource == resource) total += n.Remaining;
            }
            return total;
        }

        /// <summary>Goods of every resource consumed on purpose plus stock that went with a destroyed node. Unlike a container, a felled tree drops nothing.</summary>
        public long ConsumedTotal
        {
            get
            {
                long total = 0;
                foreach (KeyValuePair<string, long> pair in consumed) total += pair.Value;
                return total;
            }
        }

        /// <summary>The same for one resource, so <see cref="TotalOf"/> plus this is constant per resource.</summary>
        public long ConsumedOf(string resource) => consumed.TryGetValue(resource, out long amount) ? amount : 0;

        private void Count(string resource, long amount)
        {
            if (amount < 1) return;
            consumed[resource] = ConsumedOf(resource) + amount;
        }

        public int LiveReservationCount => reservations.Count;

        // ---- housekeeping ----

        public void Tick(World tickedWorld)
        {
            for (int i = reservationOrder.Count - 1; i >= 0; i--)
            {
                Reservation r = reservations[reservationOrder[i]];
                if (r.ExpiresAtTick <= tickedWorld.Tick || !tickedWorld.IsAlive(r.Holder)) Release(r);
            }

            int write = 0;
            for (int read = 0; read < holderOrder.Count; read++)
            {
                EntityId holder = holderOrder[read];
                bool isContainer = containers.TryGetValue(holder, out Container container);
                if (tickedWorld.IsAlive(holder))
                {
                    // An emptied ground pile has no reason to exist.
                    if (isContainer && container.Capacity == int.MaxValue && container.Total == 0) tickedWorld.Despawn(holder, "pile emptied");
                    holderOrder[write++] = holder;
                    continue;
                }
                if (isContainer)
                {
                    if (container.Total > 0) Drop(tickedWorld, holder, container);
                    containers.Remove(holder);
                }
                else
                {
                    // Accounted for, so "nothing appears or vanishes unrecorded" still holds when combat kills a tree.
                    Count(nodes[holder].Resource, nodes[holder].Remaining);
                    nodes.Remove(holder);
                }
                holderEntities.Remove(holder);
            }
            // Piles created by Drop were appended while the loop ran, so the loop has already visited and kept them.
            holderOrder.RemoveRange(write, holderOrder.Count - write);
        }

        /// <summary>The holder is gone: its goods fall where it stood, so nothing a task already paid for disappears with it.</summary>
        private void Drop(World tickedWorld, EntityId holder, Container container)
        {
            // The record outlives the entity's removal from the world, so the last position is still known here.
            SimVector2 position = holderEntities[holder].Position;
            Entity pile = tickedWorld.Spawn(EntityKind.GroundPile, config.GroundPileDefinitionId, SeatId.None, position);
            var ground = new Container(pile.Id, int.MaxValue);
            containers.Add(pile.Id, ground);
            holderOrder.Add(pile.Id);
            holderEntities[pile.Id] = pile;
            foreach (KeyValuePair<string, int> goods in new List<KeyValuePair<string, int>>(container.Contents))
            {
                container.Remove(goods.Key, goods.Value);
                ground.Add(goods.Key, goods.Value);
            }
            tickedWorld.Raise(new GoodsDropped(holder, pile.Id));
        }
    }
}
