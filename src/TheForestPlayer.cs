﻿using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Steamworks;
using System;
using System.Globalization;
using System.Linq;
using TheForest.Utils;
using UnityEngine;

namespace Oxide.Game.TheForest
{
    /// <summary>
    /// Represents a player, either connected or not
    /// </summary>
    public class TheForestPlayer : IPlayer, IEquatable<IPlayer>
    {
        private static Permission libPerms;

        private readonly BoltEntity entity;
        private readonly CSteamID cSteamId;
        private readonly PlayerStats stats;
        private readonly ulong steamId;

        internal TheForestPlayer(ulong id, string name)
        {
            if (libPerms == null)
            {
                libPerms = Interface.Oxide.GetLibrary<Permission>();
            }

            Name = name?.Sanitize() ?? "Unnamed";
            steamId = id;
            Id = id.ToString();
        }

        internal TheForestPlayer(BoltEntity entity)
        {
            cSteamId = SteamDSConfig.clientConnectionInfo[entity.source.ConnectionId];
            steamId = cSteamId.m_SteamID;
            Id = steamId.ToString();
            Name = (entity.GetState<IPlayerState>().name?.Sanitize() ?? Name) ?? "Unnamed";
            stats = entity.GetComponentInChildren<PlayerStats>();
            this.entity = entity;
        }

        #region Objects

        /// <summary>
        /// Gets the object that backs the player
        /// </summary>
        public object Object => entity;

        /// <summary>
        /// Gets the player's last command type
        /// </summary>
        public CommandType LastCommand { get; set; }

        #endregion Objects

        #region Information

        /// <summary>
        /// Gets/sets the name for the player
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets the ID for the player (unique within the current game)
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the player's IP address
        /// </summary>
        public string Address
        {
            get
            {
                SteamGameServerNetworking.GetP2PSessionState(cSteamId, out P2PSessionState_t sessionState);
                uint ip = sessionState.m_nRemoteIP;
                return string.Concat(ip >> 24 & 255, ".", ip >> 16 & 255, ".", ip >> 8 & 255, ".", ip & 255);
            }
        }

        /// <summary>
        /// Gets the player's average network ping
        /// </summary>
        public int Ping => Convert.ToInt32(entity.source.PingNetwork); // TODO: Test

        /// <summary>
        /// Gets the player's language
        /// </summary>
        public CultureInfo Language => CultureInfo.GetCultureInfo("en"); // TODO: Implement when possible

        /// <summary>
        /// Returns if the player is admin
        /// </summary>
        public bool IsAdmin => entity?.source?.IsDedicatedServerAdmin() ?? false; // TODO: Test

        /// <summary>
        /// Gets if the player is banned
        /// </summary>
        public bool IsBanned => CoopKick.IsBanned(steamId);

        /// <summary>
        /// Returns if the player is connected
        /// </summary>
        public bool IsConnected => BoltNetwork.clients.Contains(entity.source); // TODO: Test

        /// <summary>
        /// Returns if the player is sleeping
        /// </summary>
        public bool IsSleeping => Scene.Atmosphere.Sleeping; // TODO: Fix

        /// <summary>
        /// Returns if the player is the server
        /// </summary>
        public bool IsServer => false;

        #endregion Information

        #region Administration

        /// <summary>
        /// Bans the player for the specified reason and duration
        /// </summary>
        /// <param name="reason"></param>
        /// <param name="duration"></param>
        public void Ban(string reason, TimeSpan duration = default)
        {
            if (!IsBanned)
            {
                CoopKick.KickedPlayer kickedPlayer = new CoopKick.KickedPlayer
                {
                    Name = Name,
                    SteamId = steamId,
                    BanEndTime = duration.TotalMinutes <= 0 ? 0 : DateTime.UtcNow.ToUnixTimestamp() + (long)duration.TotalMinutes
                };
                CoopKick.Instance.kickedSteamIds.Add(kickedPlayer);
                CoopKick.SaveList();

                if (IsConnected)
                {
                    Kick(reason);
                }
            }
        }

        /// <summary>
        /// Gets the amount of time remaining on the player's ban
        /// </summary>
        public TimeSpan BanTimeRemaining
        {
            get
            {
                CoopKick.KickedPlayer kickedPlayer = CoopKick.Instance.KickedPlayers.First(k => k.SteamId == steamId);
                return kickedPlayer != null ? TimeSpan.FromTicks(kickedPlayer.BanEndTime) : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Heals the player's character by specified amount
        /// </summary>
        /// <param name="amount"></param>
        public void Heal(float amount) => stats.Health += amount; // TODO: Test

        /// <summary>
        /// Gets/sets the player's health
        /// </summary>
        public float Health
        {
            /*GameObject deadTriggerObject = player.DeadTriggerObject;
            if (deadTriggerObject != null && deadTriggerObject.activeSelf)
            {
                RespawnDeadTrigger component = deadTriggerObject.GetComponent<RespawnDeadTrigger>();
                PlayerHealed phealed = PlayerHealed.Create(GlobalTargets.Others);
                phealed.HealingItemId = component._healItemId;
                phealed.HealTarget = player.Entity;
                phealed.Send();
                component.SendMessage("SetActive", false);
            }*/

            get => stats.Health;
            set => stats.Health = value; // TODO: Test
        }

        /// <summary>
        /// Damages the player's character by specified amount
        /// </summary>
        /// <param name="amount"></param>
        public void Hurt(float amount) => stats.Hit((int)amount, true); // TODO: Test

        /// <summary>
        /// Kicks the player from the game
        /// </summary>
        /// <param name="reason"></param>
        public void Kick(string reason)
        {
            if (entity?.source != null)
            {
                BoltConnection connection = entity.source;
                CoopKickToken coopKickToken = new CoopKickToken
                {
                    KickMessage = reason,
                    Banned = false
                };
                connection.Disconnect(coopKickToken);
            }
        }

        /// <summary>
        /// Causes the player's character to die
        /// </summary>
        public void Kill() => Hurt(1000f);

        /// <summary>
        /// Gets/sets the player's maximum health
        /// </summary>
        public float MaxHealth
        {
            get
            {
                return 1000f; // TODO: Implement when possible
            }
            set
            {
                // TODO: Implement when possible
            }
        }

        /// <summary>
        /// Renames the player to specified name
        /// <param name="name"></param>
        /// </summary>
        public void Rename(string name) => entity.GetState<IPlayerState>().name = name; // TODO: Test

        /// <summary>
        /// Teleports the player's character to the specified position
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        public void Teleport(float x, float y, float z)
        {
            entity.transform.localPosition = new Vector3(x, y, z); // TODO: Fix if the game ever supports this
        }

        /// <summary>
        /// Teleports the player's character to the specified generic position
        /// </summary>
        /// <param name="pos"></param>
        public void Teleport(GenericPosition pos) => Teleport(pos.X, pos.Y, pos.Z);

        /// <summary>
        /// Unbans the player
        /// </summary>
        public void Unban()
        {
            if (IsBanned)
            {
                CoopKick.KickedPlayer kickedPlayer = CoopKick.Instance.kickedSteamIds.First(k => k.SteamId == steamId);
                if (kickedPlayer != null)
                {
                    CoopKick.Instance.kickedSteamIds.Remove(kickedPlayer);
                    CoopKick.SaveList();
                }
            }
        }

        #endregion Administration

        #region Location

        /// <summary>
        /// Gets the position of the player
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        public void Position(out float x, out float y, out float z)
        {
            Vector3 pos = entity.gameObject.transform.position;
            x = pos.x;
            y = pos.y;
            z = pos.z;
        }

        /// <summary>
        /// Gets the position of the player
        /// </summary>
        /// <returns></returns>
        public GenericPosition Position()
        {
            Vector3 pos = entity.gameObject.transform.position;
            return new GenericPosition(pos.x, pos.y, pos.z);
        }

        #endregion Location

        #region Chat and Commands

        /// <summary>
        /// Sends the specified message and prefix to the player
        /// </summary>
        /// <param name="message"></param>
        /// <param name="prefix"></param>
        /// <param name="args"></param>
        public void Message(string message, string prefix, params object[] args)
        {
            // Format the message
            message = args.Length > 0 ? string.Format(Formatter.ToUnity(message), args) : Formatter.ToUnity(message);
            string formatted = prefix != null ? $"{prefix}: {message}" : message;

            // Create and send the message
            ChatEvent chatEvent = ChatEvent.Create(entity.source);
            chatEvent.Message = formatted;
            chatEvent.Sender = entity.networkId;
            chatEvent.Send();
        }

        /// <summary>
        /// Sends the specified message to the player
        /// </summary>
        /// <param name="message"></param>
        public void Message(string message) => Message(message, null);

        /// <summary>
        /// Replies to the player with the specified message and prefix
        /// </summary>
        /// <param name="message"></param>
        /// <param name="prefix"></param>
        /// <param name="args"></param>
        public void Reply(string message, string prefix, params object[] args) => Message(message, prefix, args);

        /// <summary>
        /// Replies to the player with the specified message
        /// </summary>
        /// <param name="message"></param>
        public void Reply(string message) => Message(message, null);

        /// <summary>
        /// Runs the specified console command on the player
        /// </summary>
        /// <param name="command"></param>
        /// <param name="args"></param>
        public void Command(string command, params object[] args)
        {
            AdminCommand adminCommand = AdminCommand.Create(entity.source);
            adminCommand.Command = command;
            adminCommand.Data = string.Join(" ", Array.ConvertAll(args, x => x.ToString()));
            adminCommand.Send();
        }

        #endregion Chat and Commands

        #region Permissions

        /// <summary>
        /// Gets if the player has the specified permission
        /// </summary>
        /// <param name="perm"></param>
        /// <returns></returns>
        public bool HasPermission(string perm) => libPerms.UserHasPermission(Id, perm);

        /// <summary>
        /// Grants the specified permission on this player
        /// </summary>
        /// <param name="perm"></param>
        public void GrantPermission(string perm) => libPerms.GrantUserPermission(Id, perm, null);

        /// <summary>
        /// Strips the specified permission from this player
        /// </summary>
        /// <param name="perm"></param>
        public void RevokePermission(string perm) => libPerms.RevokeUserPermission(Id, perm);

        /// <summary>
        /// Gets if the player belongs to the specified group
        /// </summary>
        /// <param name="group"></param>
        /// <returns></returns>
        public bool BelongsToGroup(string group) => libPerms.UserHasGroup(Id, group);

        /// <summary>
        /// Adds the player to the specified group
        /// </summary>
        /// <param name="group"></param>
        public void AddToGroup(string group) => libPerms.AddUserGroup(Id, group);

        /// <summary>
        /// Removes the player from the specified group
        /// </summary>
        /// <param name="group"></param>
        public void RemoveFromGroup(string group) => libPerms.RemoveUserGroup(Id, group);

        #endregion Permissions

        #region Operator Overloads

        /// <summary>
        /// Returns if player's unique ID is equal to another player's unique ID
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(IPlayer other) => Id == other?.Id;

        /// <summary>
        /// Returns if player's object is equal to another player's object
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj) => obj is IPlayer && Id == ((IPlayer)obj).Id;

        /// <summary>
        /// Gets the hash code of the player's unique ID
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode() => Id.GetHashCode();

        /// <summary>
        /// Returns a human readable string representation of this IPlayer
        /// </summary>
        /// <returns></returns>
        public override string ToString() => $"Covalence.TheForestPlayer[{Id}, {Name}]";

        #endregion Operator Overloads
    }
}
