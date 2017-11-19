﻿using Bolt;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using TheForest.Utils;
using UdpKit;
using UnityEngine;

namespace Oxide.Game.TheForest.Libraries.Covalence
{
    /// <summary>
    /// Represents the server hosting the game instance
    /// </summary>
    public class TheForestServer : IServer
    {
        internal NetworkId NetworkId = new NetworkId();

        #region Information

        /// <summary>
        /// Gets/sets the public-facing name of the server
        /// </summary>
        public string Name
        {
            get { return CoopLobby.Instance.Info.Name ?? SteamDSConfig.ServerName; }
            set { CoopLobby.Instance.SetName(value); }
        }

        private static IPAddress address;

        /// <summary>
        /// Gets the public-facing IP address of the server, if known
        /// </summary>
        public IPAddress Address
        {
            get
            {
                try
                {
                    if (address != null) return address;

                    var webClient = new WebClient();
                    IPAddress.TryParse(webClient.DownloadString("http://api.ipify.org"), out address);
                    return address;
                }
                catch (Exception ex)
                {
                    RemoteLogger.Exception("Couldn't get server IP address", ex);
                    return new IPAddress(0);
                }
            }
        }

        /// <summary>
        /// Gets the public-facing network port of the server, if known
        /// </summary>
        public ushort Port => SteamDSConfig.ServerGamePort;

        /// <summary>
        /// Gets the version or build number of the server
        /// </summary>
        public string Version => SteamDSConfig.ServerVersion;

        /// <summary>
        /// Gets the network protocol version of the server
        /// </summary>
        public string Protocol => Version;

        /// <summary>
        /// Gets the language set by the server
        /// </summary>
        public CultureInfo Language => CultureInfo.GetCultures(CultureTypes.AllCultures).FirstOrDefault(c => c.EnglishName == Localization.language);

        /// <summary>
        /// Gets the total of players currently on the server
        /// </summary>
        public int Players => CoopLobby.Instance.Info.CurrentMembers;

        /// <summary>
        /// Gets/sets the maximum players allowed on the server
        /// </summary>
        public int MaxPlayers
        {
            get { return CoopLobby.Instance.Info.MemberLimit; }
            set { CoopLobby.Instance.SetMemberLimit(value); }
        }

        /// <summary>
        /// Gets/sets the current in-game time on the server
        /// </summary>
        public DateTime Time
        {
            get { return DateTime.Today.AddMinutes(Scene.Atmosphere.TimeOfDay); } // TODO: Fix this not working
            set { Scene.Atmosphere.TimeOfDay = value.Minute; } // TODO: Fix this not working
        }

        /// <summary>
        /// Gets information on the currently loaded save file
        /// </summary>
        public SaveInfo SaveInfo => null;

        #endregion Information

        #region Administration

        /// <summary>
        /// Bans the player for the specified reason and duration
        /// </summary>
        /// <param name="id"></param>
        /// <param name="reason"></param>
        /// <param name="duration"></param>
        public void Ban(string id, string reason, TimeSpan duration = default(TimeSpan))
        {
            if (IsBanned(id)) return;

            Scene.HudGui.MpPlayerList.Ban(Convert.ToUInt64(id));
            CoopKick.SaveList();
        }

        /// <summary>
        /// Gets the amount of time remaining on the player's ban
        /// </summary>
        /// <param name="id"></param>
        public TimeSpan BanTimeRemaining(string id)
        {
            var kickedPlayer = CoopKick.Instance.KickedPlayers.First(p => p.SteamId == Convert.ToUInt64(id));
            return kickedPlayer != null ? TimeSpan.FromTicks(kickedPlayer.BanEndTime) : TimeSpan.Zero;
        }

        /// <summary>
        /// Gets if the player is banned
        /// </summary>
        /// <param name="id"></param>
        public bool IsBanned(string id) => CoopKick.IsBanned(new UdpSteamID(Convert.ToUInt64(id)));

        /// <summary>
        /// Saves the server and any related information
        /// </summary>
        public void Save()
        {
            LevelSerializer.Checkpoint();
            SteamDSConfig.SaveGame();
        }

        /// <summary>
        /// Unbans the player
        /// </summary>
        /// <param name="id"></param>
        public void Unban(string id)
        {
            if (!IsBanned(id)) return;

            CoopKick.UnBanPlayer(ulong.Parse(id));
        }

        #endregion Administration

        #region Chat and Commands

        /// <summary>
        /// Broadcasts the specified chat message and prefix to all players
        /// </summary>
        /// <param name="message"></param>
        /// <param name="prefix"></param>
        /// <param name="args"></param>
        public void Broadcast(string message, string prefix, params object[] args) // TODO: Fix NullReferenceException
        {
            //CoopServerInfo.Instance.entity.GetState<IPlayerState>().name = prefix ?? "Server";
            var chatEvent = ChatEvent.Create(GlobalTargets.AllClients);
            chatEvent.Message = args.Length > 0 ? string.Format(Formatter.ToUnity(message), args) : Formatter.ToUnity(message);
            chatEvent.Sender = CoopServerInfo.Instance.entity.networkId;
            chatEvent.Send();

            Debug.Log($"[Chat] {message}");
        }

        /// <summary>
        /// Broadcasts the specified chat message to all players
        /// </summary>
        /// <param name="message"></param>
        public void Broadcast(string message) => Broadcast(message, null);

        /// <summary>
        /// Runs the specified server command
        /// </summary>
        /// <param name="command"></param>
        /// <param name="args"></param>
        public void Command(string command, params object[] args)
        {
            var adminCommand = AdminCommand.Create(GlobalTargets.OnlyServer);
            adminCommand.Command = command;
            adminCommand.Data = string.Concat(args.Select(o => o.ToString()).ToArray());
            adminCommand.Send();
        }

        #endregion Chat and Commands
    }
}
