using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using ServerCharacters.Models;
using UnityEngine;

namespace ServerCharacters.Systems
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; } = null!;

        private const string RpcHandshake        = "ServerCharacters_Handshake";
        private const string RpcProfileData      = "ServerCharacters_ProfileData";
        private const string RpcSaveProfile      = "ServerCharacters_SaveProfile";
        private const string RpcSaveProfileChunk = "ServerCharacters_SaveProfileChunk";
        private const string RpcProfileDataChunk = "ServerCharacters_ProfileDataChunk";

        private const int ChunkSize = 500 * 1024; // 500 KB chunks

        /// <summary>
        /// Peers that have successfully completed our handshake.
        /// The timeout coroutine checks this set — if a peer is NOT in here after the
        /// timeout window, they are kicked (no mod installed or wrong version).
        /// </summary>
        private readonly HashSet<long> _handshakeCompleted = new HashSet<long>();

        public static void Initialize()
        {
            var go = new GameObject("ServerCharacters_NetworkManager");
            Instance = go.AddComponent<NetworkManager>();
            DontDestroyOnLoad(go);
        }

        public void RegisterRPCs()
        {
            ZRoutedRpc.instance.Register<string>(RpcHandshake, RPC_Handshake);
            ZRoutedRpc.instance.Register<int, int, byte[]>(RpcProfileDataChunk, RPC_ProfileDataChunk);
            ZRoutedRpc.instance.Register<int, int, byte[]>(RpcSaveProfileChunk, RPC_SaveProfileChunk);
        }

        // ── Handshake ─────────────────────────────────────────────────────────────
        //
        // Flow:
        //   1. Server → Client:  RpcHandshake("request")
        //   2. Client → Server:  RpcHandshake(<modVersion>)
        //   3. Server validates version, marks peer as handshake-complete, sends profile data.

        private void RPC_Handshake(long sender, string version)
        {
            if (ZNet.instance.IsServer())
            {
                // Server received handshake response from client
                if (version == "request")
                {
                    // This is our own broadcast reflecting back — ignore
                    return;
                }

                if (version != Plugin.ModVersion)
                {
                    Plugin.Log.LogWarning($"[NetworkManager] Peer {sender} has wrong mod version: {version} (expected {Plugin.ModVersion}). Kicking.");
                    var wrongPeer = ZNet.instance.GetPeer(sender);
                    if (wrongPeer != null) ZNet.instance.Disconnect(wrongPeer);
                    return;
                }

                Plugin.Log.LogInfo($"[NetworkManager] Peer {sender} completed handshake (v{version}).");
                _handshakeCompleted.Add(sender);

                // Now send them their profile data
                var peer = ZNet.instance.GetPeer(sender);
                if (peer == null) return;

                string playerId = Helpers.ZNetHelper.GetPlayerId(peer);
                var snapshot = SnapshotManager.GetSnapshot(playerId);

                byte[] profileBytes;
                if (snapshot != null && snapshot.HasData)
                {
                    profileBytes = snapshot.GetProfileBytes();
                    Plugin.Log.LogInfo($"[NetworkManager] Sending existing profile to peer {sender} ({profileBytes.Length} bytes).");
                }
                else
                {
                    // First join — send a blank character (server is the source of truth)
                    profileBytes = Helpers.ProfileHelper.CreateBlankProfile(peer.m_playerName);
                    Plugin.Log.LogInfo($"[NetworkManager] First join for {playerId} — sending blank profile ({profileBytes.Length} bytes).");
                }

                SendProfileDataToClient(sender, profileBytes);
            }
            else
            {
                // Client received handshake request from server — reply with our version
                if (version == "request")
                {
                    Plugin.Log.LogInfo($"[NetworkManager] Server requested handshake. Replying with version {Plugin.ModVersion}.");
                    ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcHandshake, Plugin.ModVersion);
                }
            }
        }

        /// <summary>
        /// Called by the server when a new peer joins. Sends the handshake request and
        /// starts the timeout watchdog that kicks the peer if they never reply.
        /// </summary>
        public void SendHandshakeRequest(ZNetPeer peer)
        {
            Plugin.Log.LogInfo($"[NetworkManager] Sending handshake request to peer {peer.m_uid}...");
            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcHandshake, "request");
            StartCoroutine(HandshakeTimeout(peer));
        }

        /// <summary>
        /// Watchdog: if the peer hasn't completed the handshake within the configured
        /// timeout, they are kicked. This enforces the "client mod required" policy.
        /// </summary>
        private IEnumerator HandshakeTimeout(ZNetPeer peer)
        {
            float timeout = ModConfig.ProfileSyncTimeoutSeconds.Value;
            yield return new WaitForSeconds(timeout);

            // Still connected but never replied?
            if (ZNet.instance != null &&
                ZNet.instance.GetPeer(peer.m_uid) != null &&
                !_handshakeCompleted.Contains(peer.m_uid))
            {
                Plugin.Log.LogWarning(
                    $"[NetworkManager] Peer {peer.m_uid} timed out waiting for handshake ({timeout}s). " +
                    $"Client mod not installed or incompatible. Kicking.");
                ZNet.instance.Disconnect(peer);
            }
        }

        /// <summary>
        /// Called when a peer disconnects. Cleans up the handshake tracking set.
        /// </summary>
        public void OnPeerDisconnected(long peerId)
        {
            _handshakeCompleted.Remove(peerId);
        }

        // ── Profile data transfer ─────────────────────────────────────────────────

        private void SendProfileDataToClient(long peerId, byte[] data)
        {
            Plugin.Log.LogInfo($"[NetworkManager] Sending {data.Length} bytes of profile data to peer {peerId}");
            SendChunks(peerId, RpcProfileDataChunk, data);
        }

        public void SendProfileDataToServer(byte[] data)
        {
            Plugin.Log.LogInfo($"[NetworkManager] Sending {data.Length} bytes of profile data to server");
            // In Valheim, peer ID 0 represents the server from a client's perspective
            SendChunks(0L, RpcSaveProfileChunk, data);
        }

        private void SendChunks(long target, string rpcName, byte[] data)
        {
            int totalChunks = Mathf.CeilToInt((float)data.Length / ChunkSize);
            if (totalChunks == 0)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(target, rpcName, 0, 0, Array.Empty<byte>());
                return;
            }

            for (int i = 0; i < totalChunks; i++)
            {
                int length = Mathf.Min(ChunkSize, data.Length - (i * ChunkSize));
                byte[] chunk = new byte[length];
                Array.Copy(data, i * ChunkSize, chunk, 0, length);
                ZRoutedRpc.instance.InvokeRoutedRPC(target, rpcName, totalChunks, i, chunk);
            }
        }

        private void RPC_ProfileDataChunk(long sender, int totalChunks, int chunkIndex, byte[] chunk)
        {
            if (ZNet.instance.IsServer()) return;

            byte[] fullData = ProcessIncomingChunk(sender, totalChunks, chunkIndex, chunk);
            if (fullData != null)
            {
                Plugin.Log.LogInfo($"[NetworkManager] Client received full profile data ({fullData.Length} bytes).");
                Patches.ClientProfilePatches.ReceiveServerProfile(fullData);
            }
        }

        private void RPC_SaveProfileChunk(long sender, int totalChunks, int chunkIndex, byte[] chunk)
        {
            if (!ZNet.instance.IsServer()) return;

            byte[] fullData = ProcessIncomingChunk(sender, totalChunks, chunkIndex, chunk);
            if (fullData != null)
            {
                Plugin.Log.LogInfo($"[NetworkManager] Server received full profile data ({fullData.Length} bytes) from peer {sender}.");
                var peer = ZNet.instance.GetPeer(sender);
                if (peer == null) return;

                string playerId = Helpers.ZNetHelper.GetPlayerId(peer);
                var snapshot = SnapshotManager.CreateSnapshot(playerId, peer.m_playerName, fullData);
                SnapshotManager.SaveSnapshot(snapshot);
            }
        }

        // ── Chunk reassembly ──────────────────────────────────────────────────────

        private Dictionary<long, Dictionary<int, byte[]>> _incomingChunks = new Dictionary<long, Dictionary<int, byte[]>>();

        private byte[] ProcessIncomingChunk(long sender, int totalChunks, int chunkIndex, byte[] chunk)
        {
            if (totalChunks == 0) return Array.Empty<byte>();

            if (!_incomingChunks.ContainsKey(sender))
                _incomingChunks[sender] = new Dictionary<int, byte[]>();

            _incomingChunks[sender][chunkIndex] = chunk;

            if (_incomingChunks[sender].Count == totalChunks)
            {
                // Reconstruct full array
                List<byte> fullData = new List<byte>();
                for (int i = 0; i < totalChunks; i++)
                {
                    fullData.AddRange(_incomingChunks[sender][i]);
                }

                _incomingChunks.Remove(sender);
                return fullData.ToArray();
            }

            return Array.Empty<byte>();
        }
    }
}
