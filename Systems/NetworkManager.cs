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

        private readonly HashSet<long> _handshakeCompleted = new HashSet<long>();

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

            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcKickReason, reason);
            StartCoroutine(DisconnectRejectedPeer(peer));
        }

        private IEnumerator DisconnectRejectedPeer(ZNetPeer peer)
        {
            // Give the routed reason packet one update to reach the client before
            // Valheim closes the peer and displays its generic kick panel.
            yield return null;

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
                    if (wrongPeer != null) ZNet.instance.Disconnect(wrongPeer);
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
                Plugin.Log.LogInfo($"[CharacterVault :: Network] Checking snapshot store for SteamID {playerId} ('{peer.m_playerName}')...");

                var snapshot = SnapshotManager.GetSnapshot(playerId);

                byte[] profileBytes;
                if (snapshot != null && snapshot.HasData)
                {
                    profileBytes = snapshot.GetProfileBytes();
                    Plugin.Log.LogInfo($"[CharacterVault :: Network] Peer {sender} (SteamID {playerId}) -> SENDING EXISTING STORED PROFILE ({profileBytes.Length} bytes).");
                }
                else
                {
                    Plugin.Log.LogInfo($"[CharacterVault :: Network] Peer {sender} (SteamID {playerId}) -> NO STORED SNAPSHOT (First Join). Requesting client-side clean initialization.");
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
            Plugin.Log.LogInfo($"[CharacterVault :: Network] Initiating handshake with peer {peer.m_uid} (SteamID {peer.m_socket.GetHostName()})...");
            ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcHandshake, "request");
            StartCoroutine(HandshakeTimeout(peer));
        }

        private IEnumerator HandshakeTimeout(ZNetPeer peer)
        {
            float timeout = ModConfig.ProfileSyncTimeoutSeconds.Value;
            yield return new WaitForSeconds(timeout);

            if (ZNet.instance != null &&
                ZNet.instance.GetPeer(peer.m_uid) != null &&
                !_handshakeCompleted.Contains(peer.m_uid))
            {
                Plugin.Log.LogWarning(
                    $"[CharacterVault :: Network] Peer {peer.m_uid} TIMED OUT after {timeout}s waiting for handshake. " +
                    $"Client mod not installed or incompatible. KICKING PEER.");
                ZNet.instance.Disconnect(peer);
            }
        }

        public void OnPeerDisconnected(long peerId)
        {
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
                Plugin.Log.LogInfo($"[CharacterVault :: Network] SERVER: Received full profile upload ({fullData.Length} bytes) from peer {sender} (SteamID {playerId}, Character '{peer.m_playerName}')");

                var snapshot = SnapshotManager.CreateSnapshot(playerId, peer.m_playerName, fullData, isPlayerData);
                SnapshotManager.SaveSnapshot(snapshot);
            }
        }

        // ── Chunk reassembly ──────────────────────────────────────────────────────

        private Dictionary<long, Dictionary<int, byte[]>> _incomingChunks = new Dictionary<long, Dictionary<int, byte[]>>();

        private byte[]? ProcessIncomingChunk(long sender, int totalChunks, int chunkIndex, byte[] chunk)
        {
            if (totalChunks == 0) return Array.Empty<byte>();

            if (!_incomingChunks.ContainsKey(sender))
                _incomingChunks[sender] = new Dictionary<int, byte[]>();

            _incomingChunks[sender][chunkIndex] = chunk;

            if (_incomingChunks[sender].Count == totalChunks)
            {
                List<byte> fullData = new List<byte>();
                for (int i = 0; i < totalChunks; i++)
                {
                    fullData.AddRange(_incomingChunks[sender][i]);
                }

                _incomingChunks.Remove(sender);
                return fullData.ToArray();
            }

            return null;
        }
    }
}
