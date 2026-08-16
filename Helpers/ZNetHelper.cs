using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CharacterVault.Helpers
{
    /// <summary>
    /// Reflection-based wrappers for ZNet's private/internal members.
    ///
    /// Valheim ships a non-publicized assembly, meaning many fields we need are private.
    /// This helper caches the FieldInfo/MethodInfo at startup for efficient repeated access.
    ///
    /// If you ever publicize assembly_valheim.dll (see libs/COPY_DLLS_HERE.md), you can
    /// replace all ZNetHelper calls with direct field access and delete this file.
    /// </summary>
    internal static class ZNetHelper
    {
        // ── Cached reflection handles (resolved once at static init) ──────────────
        private static readonly FieldInfo? FiPeers = typeof(ZNet).GetField(
            "m_peers", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        private static readonly FieldInfo? FiAdminList = typeof(ZNet).GetField(
            "m_adminList", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        private static readonly MethodInfo? MiListContainsId = typeof(ZNet).GetMethod(
            "ListContainsId", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a snapshot of all currently connected peers.
        /// Safe to iterate — returns a copy so the caller won't hit modification-during-iteration issues.
        /// </summary>
        public static List<ZNetPeer> GetPeers()
        {
            if (ZNet.instance == null || FiPeers == null) return new List<ZNetPeer>();
            var raw = FiPeers.GetValue(ZNet.instance) as List<ZNetPeer>;
            return raw != null ? new List<ZNetPeer>(raw) : new List<ZNetPeer>();
        }

        /// <summary>
        /// Find a peer by its ZRpc connection object.
        /// Needed because <see cref="ZNet.GetPeer(long)"/> takes a UID, not a ZRpc.
        /// </summary>
        public static ZNetPeer? GetPeerByRpc(ZRpc rpc)
        {
            return GetPeers().FirstOrDefault(p => p.m_rpc == rpc);
        }

        /// <summary>
        /// Returns the real player ID string (e.g. Steam_1234567, Xbox_...).
        /// Handles raw numeric Steam IDs from ZSteamSocket and prefixes them with 'Steam_'.
        /// </summary>
        public static string GetPlayerId(ZNetPeer peer)
        {
            if (peer == null || peer.m_socket == null) return string.Empty;
            string host = peer.m_socket.GetHostName();
            if (string.IsNullOrWhiteSpace(host)) return string.Empty;

            // If it's a numeric 64-bit Steam ID from ZSteamSocket, prefix with "Steam_"
            if (ulong.TryParse(host, out _))
            {
                return "Steam_" + host;
            }

            return host;
        }

        /// <summary>
        /// Verifies that an ID is safe to use as a binding key and snapshot filename.
        /// Valheim reports platform IDs such as Steam_... and Xbox_... through GetHostName().
        /// </summary>
        public static bool IsValidPlayerId(string? playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId) || playerId!.Length > 128)
                return false;

            foreach (char character in playerId)
            {
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '-')
                    return false;
            }

            return playerId.StartsWith("Steam_", StringComparison.OrdinalIgnoreCase) ||
                   playerId.StartsWith("Xbox_", StringComparison.OrdinalIgnoreCase) ||
                   playerId.StartsWith("PlayFab_", StringComparison.OrdinalIgnoreCase) ||
                   ulong.TryParse(playerId, out _);
        }

        /// <summary>
        /// Returns true if the given peer is in the server's admin list (adminlist.txt).
        /// Checks both formatted platform ID and raw socket hostname.
        /// </summary>
        public static bool IsAdmin(ZNetPeer peer)
        {
            if (peer == null) return false;
            string playerId = GetPlayerId(peer);
            string host = peer.m_socket?.GetHostName() ?? string.Empty;
            return IsAdmin(playerId) || (!string.IsNullOrWhiteSpace(host) && IsAdmin(host));
        }

        /// <summary>
        /// Returns true if the given player ID is in the server's admin list (adminlist.txt).
        /// Checks candidate formats (with and without platform prefix) against Valheim's admin list.
        /// </summary>
        public static bool IsAdmin(string playerId)
        {
            if (ZNet.instance == null || string.IsNullOrWhiteSpace(playerId))
                return false;

            try
            {
                object? adminList = FiAdminList != null
                    ? FiAdminList.GetValue(ZNet.instance)
                    : Traverse.Create(ZNet.instance).Field("m_adminList").GetValue();

                if (adminList == null) return false;

                var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { playerId.Trim() };

                if (playerId.StartsWith("Steam_", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(playerId.Substring("Steam_".Length).Trim());
                }
                else if (ulong.TryParse(playerId.Trim(), out _))
                {
                    candidates.Add("Steam_" + playerId.Trim());
                }

                if (playerId.StartsWith("Xbox_", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(playerId.Substring("Xbox_".Length).Trim());
                }

                if (playerId.StartsWith("PlayFab_", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(playerId.Substring("PlayFab_".Length).Trim());
                }

                // 1. Check via ZNet.ListContainsId method
                if (MiListContainsId != null)
                {
                    foreach (string candidate in candidates)
                    {
                        if ((bool)(MiListContainsId.Invoke(ZNet.instance, new object[] { adminList, candidate }) ?? false))
                            return true;
                    }
                }

                // 2. Check via SyncedList.Contains method
                MethodInfo? containsMethod = adminList.GetType().GetMethod("Contains", new[] { typeof(string) });
                if (containsMethod != null)
                {
                    foreach (string candidate in candidates)
                    {
                        if ((bool)(containsMethod.Invoke(adminList, new object[] { candidate }) ?? false))
                            return true;
                    }
                }

                // 3. Check via SyncedList.GetList()
                MethodInfo? getListMethod = adminList.GetType().GetMethod("GetList");
                if (getListMethod != null)
                {
                    var listObj = getListMethod.Invoke(adminList, null);
                    if (listObj is IEnumerable enumerable)
                    {
                        foreach (object item in enumerable)
                        {
                            if (item is string s && candidates.Contains(s.Trim()))
                                return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[ZNetHelper] IsAdmin check failed: {ex.Message}");
                return false;
            }
        }
    }
}
