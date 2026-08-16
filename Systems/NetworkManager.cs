using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using CharacterVault.Models;
using UnityEngine;

namespace CharacterVault.Systems
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; } = null!;

        private const string RpcHandshake        = "CharacterVault_Handshake";
        private const string RpcProfileData      = "CharacterVault_ProfileData";
        private const string RpcSaveProfile      = "CharacterVault_SaveProfile";
        private const string RpcSaveProfileChunk = "CharacterVault_SaveProfileChunk";
        private const string RpcProfileDataChunk = "CharacterVault_ProfileDataChunk";
        private const string RpcKickReason       = "CharacterVault_KickReason";

        private const int ChunkSize = 500 * 1024; // 500 KB chunks
        private const int MaxProfileBytes = 64 * 1024 * 1024;
        private const float IncomingTransferTimeoutSeconds = 120f;

        private readonly HashSet<long> _handshakeCompleted = new HashSet<long>();
        private readonly Dictionary<long, IncomingTransfer> _incomingChunks = new Dictionary<long, IncomingTransfer>();
        private float _nextIncomingTransferCleanup;

        private sealed class IncomingTransfer
        {
            public IncomingTransfer(int totalChunks)
            {
                TotalChunks = totalChunks;
                LastUpdated = Time.unscaledTime;
            }

            public int TotalChunks { get; }
            public Dictionary<int, byte[]> Chunks { get; } = new Dictionary<int, byte[]>();
            public int TotalBytes { get; set; }
            public float LastUpdated { get; set; }
        }

        public static void Initialize()
        {
            var go = new GameObject("CharacterVault_NetworkManager");
            Instance = go.AddComponent<NetworkManager>();
            DontDestroyOnLoad(go);
            Plugin.Log.LogInfo("[CharacterVault :: Network] NetworkManager initialized.");
        }

        public void RegisterRPCs()
        {
            ZRoutedRpc.instance.Register<string>(RpcHandshake, RPC_Handshake);
            ZRoutedRpc.instance.Register<int, int, bool, bool, ZPackage>(RpcProfileDataChunk, RPC_ProfileDataChunk);
            ZRoutedRpc.instance.Register<int, int, bool, bool, ZPackage>(RpcSaveProfileChunk, RPC_SaveProfileChunk);
            ZRoutedRpc.instance.Register<string>(RpcKickReason, RPC_KickReason);
            AdminCommandHandler.RegisterRPCs();
            Plugin.Log.LogInfo("[CharacterVault :: Network] RPC handlers registered.");
        }

        private void RPC_KickReason(long sender, string reason)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer()) return;

            ConnectionRejectionManager.SetReason(reason);
            Plugin.Log.LogWarning($"[CharacterVault :: Network] Server rejection reason: {reason}");
        }

        public void RejectPeer(ZNetPeer peer, string reason)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            Plugin.Log.LogWarning($"[CharacterVault :: Network] Rejecting peer {peer.m_uid} (platform ID {Helpers.ZNetHelper.GetPlayerId(peer)}): {reason}");
            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcKickReason, reason);
            StartCoroutine(DisconnectRejectedPeer(peer));
        }

        private IEnumerator DisconnectRejectedPeer(ZNetPeer peer)
        {
            // Give the routed reason packet enough time to reach the client across the network
            // before Valheim closes the peer socket and displays the kick panel.
            yield return new WaitForSecondsRealtime(1.0f);

            if (ZNet.instance == null || ZNet.instance.GetPeer(peer.m_uid) != peer)
                yield break;

            peer.m_rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorKicked);
            ZNet.instance.Disconnect(peer);
        }

        // ── Handshake ─────────────────────────────────────────────────────────────

        private void RPC_Handshake(long sender, string version)
        {
            if (ZNet.instance.IsServer())
            {
                if (version == "request") return;

                if (version != Plugin.ModVersion)
                {
                    Plugin.Log.LogWarning($"[CharacterVault :: Network] Peer {sender} wrong mod version: '{version}' (expected '{Plugin.ModVersion}'). KICKING.");
                    var wrongPeer = ZNet.instance.GetPeer(sender);
                    if (wrongPeer != null) RejectPeer(wrongPeer, $"CharactersVault version mismatch: client has v{version}, server requires v{Plugin.ModVersion}");
                    return;
                }

                Plugin.Log.LogInfo($"[CharacterVault :: Network] Peer {sender} COMPLETED HANDSHAKE (v{version}).");
                _handshakeCompleted.Add(sender);

                var peer = ZNet.instance.GetPeer(sender);
                if (peer == null)
                {
                    Plugin.Log.LogWarning($"[CharacterVault :: Network] Peer {sender} null after handshake!");
                    return;
                }

                string playerId = Helpers.ZNetHelper.GetPlayerId(peer);
                Plugin.Log.LogInfo($"[CharacterVault :: Network] Checking snapshot store for platform ID {playerId} ('{peer.m_playerName}')...");

                var snapshot = SnapshotManager.GetSnapshot(playerId);

                byte[] profileBytes;
                if (snapshot != null && snapshot.HasData)
                {
                    profileBytes = snapshot.GetProfileBytes();
                    Plugin.Log.LogInfo($"[CharacterVault :: Network] Peer {sender} (platform ID {playerId}) -> sending existing stored profile ({profileBytes.Length} bytes).");
                }
                else
                {
                    Plugin.Log.LogInfo($"[CharacterVault :: Network] Peer {sender} (platform ID {playerId}) -> no stored snapshot (first join). Requesting client-side clean initialization.");
                    profileBytes = Array.Empty<byte>();
                }

                SendProfileDataToClient(sender, profileBytes, snapshot != null && snapshot.IsPlayerData, snapshot == null);
            }
            else
            {
                if (version == "request")
                {
                    Plugin.Log.LogInfo($"[CharacterVault :: Network] Server requested handshake. Replying with version '{Plugin.ModVersion}'.");
                    ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcHandshake, Plugin.ModVersion);
                }
            }
        }

        public void SendHandshakeRequest(ZNetPeer peer)
        {
            Plugin.Log.LogInfo($"[CharacterVault :: Network] Initiating handshake with peer {peer.m_uid} (platform ID {peer.m_socket.GetHostName()})...");
            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcHandshake, "request");
            StartCoroutine(HandshakeTimeout(peer));
        }

        private IEnumerator HandshakeTimeout(ZNetPeer peer)
        {
            float timeout = ModConfig.ProfileSyncTimeoutSeconds.Value;
            yield return new WaitForSecondsRealtime(timeout);

            if (ZNet.instance != null &&
                ZNet.instance.GetPeer(peer.m_uid) != null &&
                !_handshakeCompleted.Contains(peer.m_uid))
            {
                Plugin.Log.LogWarning(
                    $"[CharacterVault :: Network] Peer {peer.m_uid} TIMED OUT after {timeout}s waiting for handshake. " +
                    $"Client mod not installed or incompatible. KICKING PEER.");
                RejectPeer(peer, "CharactersVault handshake timeout: server did not receive client handshake reply.");
            }
        }

        public void OnPeerDisconnected(long peerId)
        {
            _incomingChunks.Remove(peerId);

            if (_handshakeCompleted.Remove(peerId))
            {
                Plugin.Log.LogInfo($"[CharacterVault :: Network] Cleaned up handshake tracking for disconnected peer {peerId}.");
            }
        }

        // ── Profile data transfer ─────────────────────────────────────────────────

        private void SendProfileDataToClient(long peerId, byte[] data, bool isPlayerData, bool isFirstJoin)
        {
            Plugin.Log.LogInfo($"[CharacterVault :: Network] Transmitting {data.Length} profile bytes to client peer {peerId}...");
            SendChunks(peerId, RpcProfileDataChunk, data, isPlayerData, isFirstJoin);
        }

        public void SendProfileDataToServer(byte[] data, bool isPlayerData = false)
        {
            Plugin.Log.LogInfo($"[CharacterVault :: Network] Transmitting {data.Length} profile bytes to server...");
            SendChunks(0L, RpcSaveProfileChunk, data, isPlayerData, false);
        }

        private void SendChunks(long target, string rpcName, byte[] data, bool isPlayerData, bool isFirstJoin)
        {
            int totalChunks = Mathf.CeilToInt((float)data.Length / ChunkSize);
            if (totalChunks == 0)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(target, rpcName, 0, 0, isPlayerData, isFirstJoin, new ZPackage());
                return;
            }

            for (int i = 0; i < totalChunks; i++)
            {
                int length = Mathf.Min(ChunkSize, data.Length - (i * ChunkSize));
                byte[] chunk = new byte[length];
                Array.Copy(data, i * ChunkSize, chunk, 0, length);
                ZRoutedRpc.instance.InvokeRoutedRPC(target, rpcName, totalChunks, i, isPlayerData, isFirstJoin, new ZPackage(chunk));
            }
        }

        private void RPC_ProfileDataChunk(long sender, int totalChunks, int chunkIndex, bool isPlayerData, bool isFirstJoin, ZPackage chunk)
        {
            if (ZNet.instance.IsServer()) return;

            byte[]? fullData = ProcessIncomingChunk(sender, totalChunks, chunkIndex, chunk.GetArray());
            if (fullData != null)
            {
                Plugin.Log.LogInfo($"[CharacterVault :: Network] CLIENT: Full profile data reassembled ({fullData.Length} bytes). Passing to ClientProfilePatches.");
                Patches.ClientProfilePatches.ReceiveServerProfile(fullData, isPlayerData, isFirstJoin);
            }
        }

        private void RPC_SaveProfileChunk(long sender, int totalChunks, int chunkIndex, bool isPlayerData, bool isFirstJoin, ZPackage chunk)
        {
            if (!ZNet.instance.IsServer()) return;

            byte[]? fullData = ProcessIncomingChunk(sender, totalChunks, chunkIndex, chunk.GetArray());
            if (fullData != null)
            {
                var peer = ZNet.instance.GetPeer(sender);
                if (peer == null) return;

                string playerId = Helpers.ZNetHelper.GetPlayerId(peer);
                Plugin.Log.LogInfo($"[CharacterVault :: Network] Server received full profile upload ({fullData.Length} bytes) from peer {sender} (platform ID {playerId}, Character '{peer.m_playerName}')");

                var snapshot = SnapshotManager.CreateSnapshot(playerId, peer.m_playerName, fullData, isPlayerData);
                SnapshotManager.SaveSnapshot(snapshot);
            }
        }

        // ── Chunk reassembly ──────────────────────────────────────────────────────

        private byte[]? ProcessIncomingChunk(long sender, int totalChunks, int chunkIndex, byte[] chunk)
        {
            if (totalChunks == 0) return Array.Empty<byte>();

            int maximumChunkCount = Mathf.CeilToInt((float)MaxProfileBytes / ChunkSize);
            if (totalChunks < 1 || totalChunks > maximumChunkCount ||
                chunkIndex < 0 || chunkIndex >= totalChunks || chunk.Length > ChunkSize)
            {
                Plugin.Log.LogWarning($"[CharacterVault :: Network] Discarded malformed chunk transfer from peer {sender}.");
                _incomingChunks.Remove(sender);
                return null;
            }

            if (!_incomingChunks.TryGetValue(sender, out IncomingTransfer? transfer) ||
                transfer.TotalChunks != totalChunks)
            {
                transfer = new IncomingTransfer(totalChunks);
                _incomingChunks[sender] = transfer;
            }

            transfer.LastUpdated = Time.unscaledTime;
            if (transfer.Chunks.TryGetValue(chunkIndex, out byte[]? previousChunk))
                transfer.TotalBytes -= previousChunk.Length;

            transfer.TotalBytes += chunk.Length;
            if (transfer.TotalBytes > MaxProfileBytes)
            {
                Plugin.Log.LogWarning($"[CharacterVault :: Network] Discarded oversized profile transfer from peer {sender}.");
                _incomingChunks.Remove(sender);
                return null;
            }

            transfer.Chunks[chunkIndex] = chunk;

            if (transfer.Chunks.Count == totalChunks)
            {
                using var stream = new MemoryStream(transfer.TotalBytes);
                for (int i = 0; i < totalChunks; i++)
                {
                    if (!transfer.Chunks.TryGetValue(i, out byte[]? chunkData))
                    {
                        _incomingChunks.Remove(sender);
                        return null;
                    }

                    stream.Write(chunkData, 0, chunkData.Length);
                }

                _incomingChunks.Remove(sender);
                return stream.ToArray();
            }

            return null;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextIncomingTransferCleanup)
                return;

            _nextIncomingTransferCleanup = Time.unscaledTime + 10f;
            var stalePeers = new List<long>();
            foreach (var entry in _incomingChunks)
            {
                if (Time.unscaledTime - entry.Value.LastUpdated > IncomingTransferTimeoutSeconds)
                    stalePeers.Add(entry.Key);
            }

            foreach (long peerId in stalePeers)
            {
                _incomingChunks.Remove(peerId);
                Plugin.Log.LogWarning($"[CharacterVault :: Network] Discarded incomplete profile transfer from peer {peerId} after timeout.");
            }
        }
    }
}
