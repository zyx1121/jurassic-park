using System;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class SeatIdTests
    {
        [Test]
        public void SeatsWithTheSameValueAreEqual()
        {
            Assert.That(new SeatId(2), Is.EqualTo(new SeatId(2)));
            Assert.That(new SeatId(2) != new SeatId(3), Is.True);
        }

        [Test]
        public void ZeroIsTheUnownedSeat()
        {
            Assert.That(SeatId.None.IsNone, Is.True);
            Assert.That(new SeatId(1).IsNone, Is.False);
        }

        [Test]
        public void NegativeSeatsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SeatId(-1));
        }
    }
}
