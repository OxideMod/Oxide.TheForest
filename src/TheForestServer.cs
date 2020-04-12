using Bolt;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Steamworks;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using TheForest.Utils;
using UdpKit;

namespace Oxide.Game.TheForest
{
    /// <summary>
    /// Represents the server hosting the game instance
    /// </summary>
    public class TheForestServer : IServer
    {
        #region Information

        /// <summary>
        /// Gets/sets the public-facing name of the server
        /// </summary>
        public string Name
        {
            get => CoopLobby.Instance.Info.Name ?? SteamDSConfig.ServerName;
            set => CoopLobby.Instance.SetName(value);
        }

        private static IPAddress address;
        private static IPAddress localAddress;

        /// <summary>
        /// Gets the public-facing IP address of the server, if known
        /// </summary>
        public IPAddress Address
        {
            get
            {
                try
                {
                    if (address == null)
                    {
                        uint ip;
                        if (Utility.ValidateIPv4(SteamDSConfig.ServerAddress) && !Utility.IsLocalIP(SteamDSConfig.ServerAddress))
                        {
                            IPAddress.TryParse(SteamDSConfig.ServerAddress, out address);
                            Interface.Oxide.LogInfo($"IP address from command-line: {address}");
                        }
                        else if ((ip = SteamGameServer.GetPublicIP()) > 0)
                        {
                            string publicIp = string.Concat(ip >> 24 & 255, ".", ip >> 16 & 255, ".", ip >> 8 & 255, ".", ip & 255); // TODO: uint IP address utility method
                            IPAddress.TryParse(publicIp, out address);
                            Interface.Oxide.LogInfo($"IP address from Steam query: {address}");
                        }
                        else
                        {
                            WebClient webClient = new WebClient();
                            IPAddress.TryParse(webClient.DownloadString("http://api.ipify.org"), out address);
                            Interface.Oxide.LogInfo($"IP address from external API: {address}");
                        }
                    }

                    return address;
                }
                catch (Exception ex)
                {
                    RemoteLogger.Exception("Couldn't get server's public IP address", ex);
                    return IPAddress.Any;
                }
            }
        }

        /// <summary>
        /// Gets the local IP address of the server, if known
        /// </summary>
        public IPAddress LocalAddress
        {
            get
            {
                try
                {
                    return localAddress ?? (localAddress = Utility.GetLocalIP());
                }
                catch (Exception ex)
                {
                    RemoteLogger.Exception("Couldn't get server's local IP address", ex);
                    return IPAddress.Any;
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
            get => CoopLobby.Instance.Info.MemberLimit;
            set => CoopLobby.Instance.SetMemberLimit(value);
        }

        /// <summary>
        /// Gets/sets the current in-game time on the server
        /// </summary>
        public DateTime Time
        {
            get => DateTime.Today.AddMinutes(Scene.Atmosphere.TimeOfDay); // TODO: Fix this not working
            set => Scene.Atmosphere.TimeOfDay = value.Minute; // TODO: Fix this not working
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
        public void Ban(string id, string reason, TimeSpan duration = default)
        {
            if (!IsBanned(id))
            {
                if (ulong.TryParse(id, out ulong steamId))
                {
                    Scene.HudGui.MpPlayerList.Ban(steamId);
                    CoopKick.SaveList();
                }
            }
        }

        /// <summary>
        /// Gets the amount of time remaining on the player's ban
        /// </summary>
        /// <param name="id"></param>
        public TimeSpan BanTimeRemaining(string id)
        {
            if (ulong.TryParse(id, out ulong steamId))
            {
                CoopKick.KickedPlayer kickedPlayer = CoopKick.Instance.KickedPlayers.First(p => p.SteamId == steamId);
                return kickedPlayer != null ? TimeSpan.FromTicks(kickedPlayer.BanEndTime) : TimeSpan.Zero;
            }

            return TimeSpan.Zero;
        }

        /// <summary>
        /// Gets if the player is banned
        /// </summary>
        /// <param name="id"></param>
        public bool IsBanned(string id)
        {
            return ulong.TryParse(id, out ulong steamId) && CoopKick.IsBanned(new UdpSteamID(steamId));
        }

        /// <summary>
        /// Gets if the player is connected
        /// </summary>
        /// <param name="id"></param>
        public bool IsConnected(string id)
        {
            return ulong.TryParse(id, out ulong steamId) && SteamDSConfig.clientConnectionInfo.Any(c => c.Value == new CSteamID(steamId));
        }

        /// <summary>
        /// Kicks the player for the specified reason
        /// </summary>
        /// <param name="id"></param>
        /// <param name="reason"></param>
        public void Kick(string id, string reason)
        {
            if (ulong.TryParse(id, out ulong steamId))
            {
                uint connectionId = SteamDSConfig.clientConnectionInfo.First(c => c.Value == new CSteamID(steamId)).Key; // TODO: This might error
                BoltConnection connection = BoltNetwork.connections.First(c => c.ConnectionId == connectionId);
                CoopKickToken coopKickToken = new CoopKickToken
                {
                    KickMessage = reason,
                    Banned = false
                };
                connection.Disconnect(coopKickToken);
            }
        }

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
            if (IsBanned(id))
            {
                if (ulong.TryParse(id, out ulong steamId))
                {
                    CoopKick.UnBanPlayer(steamId);
                }
            }
        }

        #endregion Administration

        #region Chat and Commands

        /// <summary>
        /// Broadcasts the specified chat message and prefix to all players
        /// </summary>
        /// <param name="message"></param>
        /// <param name="prefix"></param>
        /// <param name="args"></param>
        public void Broadcast(string message, string prefix, params object[] args)
        {
            // Format the message
            message = args.Length > 0 ? string.Format(Formatter.ToUnity(message), args) : Formatter.ToUnity(message);
            string formatted = prefix != null ? $"{prefix}: {message}" : message;

            foreach (BoltEntity entity in BoltNetwork.entities)
            {
                if (entity != null && entity.StateIs<IPlayerState>())
                {
                    // Create and send the message
                    ChatEvent chatEvent = ChatEvent.Create(entity.source);
                    chatEvent.Message = formatted;
                    chatEvent.Sender = entity.networkId;
                    chatEvent.Send();
                }
            }

            Interface.Oxide.LogInfo($"[Broadcast] {message}");
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
            AdminCommand adminCommand = AdminCommand.Create(GlobalTargets.OnlyServer);
            adminCommand.Command = command;
            adminCommand.Data = string.Join(" ", Array.ConvertAll(args, x => x.ToString()));
            adminCommand.Send();
        }

        #endregion Chat and Commands
    }
}
