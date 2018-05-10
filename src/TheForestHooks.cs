﻿using Oxide.Core;
using Oxide.Core.Plugins;
using Steamworks;
using System.Linq;
using TheForest.Utils;
using UnityEngine;

namespace Oxide.Game.TheForest
{
    /// <summary>
    /// Game hooks and wrappers for the core The Forest plugin
    /// </summary>
    public partial class TheForestCore : CSPlugin
    {
        #region Player Hooks

        /// <summary>
        /// Called when the player is attempting to connect
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        [HookMethod("IOnUserApprove")]
        private object IOnUserApprove(BoltConnection connection)
        {
            ulong id = SteamDSConfig.clientConnectionInfo[connection.ConnectionId].m_SteamID;
            string idString = id.ToString();
            CSteamID cSteamId = new CSteamID(id);

            // Let covalence know
            Covalence.PlayerManager.PlayerJoin(id, "Unnamed");

            // Get IP address from Steam
            P2PSessionState_t sessionState;
            SteamGameServerNetworking.GetP2PSessionState(cSteamId, out sessionState);
            var remoteIp = sessionState.m_nRemoteIP;
            var ip = string.Concat(remoteIp >> 24 & 255, ".", remoteIp >> 16 & 255, ".", remoteIp >> 8 & 255, ".", remoteIp & 255);


            // Call out and see if we should reject
            if (canLogin is string || (canLogin is bool && !(bool)canLogin))
            object canLogin = Interface.Call("CanClientLogin", connection) ?? Interface.Call("CanUserLogin", "Unnamed", idString, ip); // TODO: Localization
            {
                var coopKickToken = new CoopKickToken
                {
                    KickMessage = canLogin is string ? canLogin.ToString() : "Connection was rejected", // TODO: Localization
                    Banned = false
                };
                connection.Disconnect(coopKickToken);
                return true;
            }

            // Call game and covalence hooks
            return Interface.Call("OnUserApprove", connection) ?? Interface.Call("OnUserApproved", "Unnamed", idString, ip); // TODO: Localization
        }

        /// <summary>
        /// Called when the player sends a message
        /// </summary>
        /// <param name="evt"></param>
        [HookMethod("IOnPlayerChat")]
        private object IOnPlayerChat(ChatEvent evt)
        {
            var entity = Scene.SceneTracker.allPlayerEntities.FirstOrDefault(ent => ent.networkId == evt.Sender);
            if (entity == null) return null;

            var name = entity.GetState<IPlayerState>().name;

            Debug.Log($"[Chat] {name}: {evt.Message}");
            ulong id = SteamDSConfig.clientConnectionInfo[entity.source.ConnectionId].m_SteamID;

            // Call game and covalence hooks
            var iplayer = Covalence.PlayerManager.FindPlayerById(id.ToString());
            return Interface.Call("OnPlayerChat", entity, evt.Message) ?? (iplayer != null ? Interface.Call("OnUserChat", iplayer, evt.Message) : null);
        }

        /// <summary>
        /// Called when the player has connected
        /// </summary>
        /// <param name="entity"></param>
        [HookMethod("OnPlayerConnected")]
        private void OnPlayerConnected(BoltEntity entity)
        {
            string id = SteamDSConfig.clientConnectionInfo[entity.source.ConnectionId].m_SteamID.ToString();
            string name = entity.GetState<IPlayerState>().name?.Sanitize() ?? "Unnamed";

            // Update player's permissions group and name
            if (permission.IsLoaded)
            {
                permission.UpdateNickname(id, name);
                var defaultGroups = Interface.Oxide.Config.Options.DefaultGroups;
                if (!permission.UserHasGroup(id, defaultGroups.Players)) permission.AddUserGroup(id, defaultGroups.Players);
                if (entity.source.IsDedicatedServerAdmin() && !permission.UserHasGroup(id, defaultGroups.Administrators)) permission.AddUserGroup(id, defaultGroups.Administrators);
            }

            Debug.Log($"{id}/{name} joined");

            // Let covalence know
            Covalence.PlayerManager.PlayerConnected(entity);
            var iplayer = Covalence.PlayerManager.FindPlayerById(id);
            if (iplayer != null) Interface.Call("OnUserConnected", iplayer);
        }

        /// <summary>
        /// Called when the player has disconnected
        /// </summary>
        /// <param name="connection"></param>
        [HookMethod("IOnPlayerDisconnected")]
        private void IOnPlayerDisconnected(BoltConnection connection)
        {
            string id = SteamDSConfig.clientConnectionInfo[connection.ConnectionId].m_SteamID.ToString();
            BoltEntity entity = Scene.SceneTracker.allPlayerEntities.FirstOrDefault(ent => ent.source.ConnectionId == connection.ConnectionId);
            if (entity == null)
            {
                return;
            }

            string name = entity.GetState<IPlayerState>().name ?? "Unnamed";

            Debug.Log($"{id}/{name} quit");

            // Call game-specific hook
            Interface.Call("OnPlayerDisconnected", entity);

            // Let covalence know
            IPlayer iplayer = Covalence.PlayerManager.FindPlayerById(id);
            if (iplayer != null)
            {
                Interface.Call("OnUserDisconnected", iplayer, "Unknown"); // TODO: Localization
            }

            Covalence.PlayerManager.PlayerDisconnected(entity);
        }

        /// <summary>
        /// Called when the player spawns
        /// </summary>
        /// <param name="entity"></param>
        [HookMethod("OnPlayerSpawn")]
        private void OnPlayerSpawn(BoltEntity entity)
        {
            string id = SteamDSConfig.clientConnectionInfo[entity.source.ConnectionId].m_SteamID.ToString();
            string name = entity.GetState<IPlayerState>().name ?? "Unnamed";

            // Call universal hook
            IPlayer iplayer = Covalence.PlayerManager.FindPlayerById(id);
            if (iplayer != null)
            {
                Interface.Call("OnUserSpawn", iplayer);
            }
        }

        #endregion Player Hooks
    }
}
