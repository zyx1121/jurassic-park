using System;
using JurassicPark.Core;
using JurassicPark.Player;
using JurassicPark.Scene;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace JurassicPark.Net
{
    /// <summary>
    /// Networked player glue: the owner drives input and its NetworkTransform; facing and the
    /// current sprite clip travel as NetworkVariables so everyone sees the same animation. The
    /// server places the player at the spawn point; the owner points the follow camera at itself.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class NetPlayer : NetworkBehaviour
    {
        private readonly NetworkVariable<int> facing = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<FixedString32Bytes> clip = new NetworkVariable<FixedString32Bytes>("Idle", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PlayerController controller;
        private SpriteSheetAnimator animator;
        private PlayerSpriteAnimator localAnimator;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            animator = GetComponentInChildren<SpriteSheetAnimator>();
            localAnimator = GetComponent<PlayerSpriteAnimator>();
        }

        public string ClipValue => clip.Value.ToString();
        public int FacingValue => facing.Value;

        public override void OnNetworkSpawn()
        {
            // The owner has transform authority, so the owner places itself at the spawn point
            if (IsOwner)
            {
                Transform spawn = NetLobby.FindSpawn();
                if (spawn != null)
                {
                    Vector3 p = spawn.position + Vector3.up * 0.1f + new Vector3(OwnerClientId * 1.5f, 0f, 0f);
                    CharacterController cc = GetComponent<CharacterController>();
                    cc.enabled = false;
                    transform.position = p;
                    cc.enabled = true;
                    NetworkTransform nt = GetComponent<NetworkTransform>();
                    if (nt != null) nt.Teleport(p, transform.rotation, transform.localScale);
                }
            }

            // Only the owner reads devices; remote copies are driven by the NetworkTransform
            controller.enabled = IsOwner;
            if (localAnimator != null) localAnimator.enabled = IsOwner;
            if (IsOwner)
            {
                FollowCamera cam = FindFirstObjectByType<FollowCamera>();
                if (cam != null) cam.Target = transform;
                if (HasArg("--autowalk")) controller.OverrideMove = new Vector2(1f, 0f);
            }
            else
            {
                facing.OnValueChanged += (_, v) => { if (animator != null) animator.Row = v; };
                clip.OnValueChanged += (_, v) => animator?.Play(v.ToString());
                if (animator != null) { animator.Row = facing.Value; animator.Play(clip.Value.ToString()); }
            }

            name = IsOwner ? "Player (local)" : $"Player (client {OwnerClientId})";
        }

        private void LateUpdate()
        {
            if (!IsSpawned || !IsOwner || animator == null) return;
            int f = (int)controller.Facing;
            if (facing.Value != f) facing.Value = f;
            string c = animator.Current != null ? animator.Current.name : "Idle";
            if (clip.Value.ToString() != c) clip.Value = c;
        }

        private static bool HasArg(string flag) => Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;
    }
}
