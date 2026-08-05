using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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

        static ZNetHelper()
        {
            if (FiPeers == null)
                Plugin.Log?.LogWarning("[ZNetHelper] ZNet.m_peers not found — peer enumeration unavailable.");
            if (FiAdminList == null || MiListContainsId == null)
                Plugin.Log?.LogWarning("[ZNetHelper] Admin list fields not found — /sc commands unavailable.");
        }

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
        /// </summary>
        public static string GetPlayerId(ZNetPeer peer)
        {
            return peer?.m_socket?.GetHostName() ?? "";
        }

        /// <summary>
        /// Returns true if the given player ID is in the server's admin list (adminlist.txt).
        /// </summary>
        public static bool IsAdmin(string playerId)
        {
            if (ZNet.instance == null || FiAdminList == null || MiListContainsId == null)
                return false;
            try
            {
                object? adminList = FiAdminList.GetValue(ZNet.instance);
                if (adminList == null) return false;
                return (bool)(MiListContainsId.Invoke(ZNet.instance,
                    new object[] { adminList, playerId }) ?? false);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[ZNetHelper] IsAdmin check failed: {ex.Message}");
                return false;
            }
        }
    }
}
