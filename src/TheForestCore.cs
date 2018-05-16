using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.TheForest.Libraries.Covalence;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Text;

namespace Oxide.Game.TheForest
{
    /// <summary>
    /// The core The Forest plugin
    /// </summary>
    public partial class TheForestCore : CSPlugin
    {
        #region Initialization

        /// <summary>
        /// Initializes a new instance of the TheForestCore class
        /// </summary>
        public TheForestCore()
        {
            // Set plugin info attributes
            Title = "The Forest";
            Author = TheForestExtension.AssemblyAuthors;
            Version = TheForestExtension.AssemblyVersion;
        }

        // Libraries
        //internal readonly Command cmdlib = Interface.Oxide.GetLibrary<Command>();
        internal readonly Lang lang = Interface.Oxide.GetLibrary<Lang>();
        internal readonly Permission permission = Interface.Oxide.GetLibrary<Permission>();
        //internal readonly Player Player = Interface.Oxide.GetLibrary<Player>();

        // Instances
        internal static readonly TheForestCovalenceProvider Covalence = TheForestCovalenceProvider.Instance;
        internal readonly PluginManager pluginManager = Interface.Oxide.RootPluginManager;
        internal readonly IServer Server = Covalence.CreateServer();

        // Commands that a plugin can't override
        internal static IEnumerable<string> RestrictedCommands => new[]
        {
            ""
        };

        private bool serverInitialized;

        #endregion Initialization

        #region Core Hooks

        [HookMethod("Init")]
        private void Init()
        {
            // Configure remote error logging
            RemoteLogger.SetTag("game", Title.ToLower());
            RemoteLogger.SetTag("game version", Server.Version);

            // Add core plugin commands
            AddCovalenceCommand(new[] { "oxide.plugins", "o.plugins", "plugins" }, "PluginsCommand", "oxide.plugins");
            AddCovalenceCommand(new[] { "oxide.load", "o.load", "plugin.load" }, "LoadCommand", "oxide.load");
            AddCovalenceCommand(new[] { "oxide.reload", "o.reload", "plugin.reload" }, "ReloadCommand", "oxide.reload");
            AddCovalenceCommand(new[] { "oxide.unload", "o.unload", "plugin.unload" }, "UnloadCommand", "oxide.unload");

            // Add core permission commands
            AddCovalenceCommand(new[] { "oxide.grant", "o.grant", "perm.grant" }, "GrantCommand", "oxide.grant");
            AddCovalenceCommand(new[] { "oxide.group", "o.group", "perm.group" }, "GroupCommand", "oxide.group");
            AddCovalenceCommand(new[] { "oxide.revoke", "o.revoke", "perm.revoke" }, "RevokeCommand", "oxide.revoke");
            AddCovalenceCommand(new[] { "oxide.show", "o.show", "perm.show" }, "ShowCommand", "oxide.show");
            AddCovalenceCommand(new[] { "oxide.usergroup", "o.usergroup", "perm.usergroup" }, "UserGroupCommand", "oxide.usergroup");

            // Add core misc commands
            AddCovalenceCommand(new[] { "oxide.lang", "o.lang" }, "LangCommand");
            AddCovalenceCommand(new[] { "oxide.save", "o.save" }, "SaveCommand");
            AddCovalenceCommand(new[] { "oxide.version", "o.version" }, "VersionCommand");

            // Register messages for localization
            foreach (KeyValuePair<string, Dictionary<string, string>> language in Core.Localization.languages)
            {
                lang.RegisterMessages(language.Value, this, language.Key);
            }

            // Setup default permission groups
            if (permission.IsLoaded)
            {
                int rank = 0;

                foreach (string defaultGroup in Interface.Oxide.Config.Options.DefaultGroups)
                {
                    if (!permission.GroupExists(defaultGroup))
                    {
                        permission.CreateGroup(defaultGroup, defaultGroup, rank++);
                    }
                }

                permission.RegisterValidate(s =>
                {
                    ulong temp;

                    if (!ulong.TryParse(s, out temp))
                    {
                        return false;
                    }

                    int digits = temp == 0 ? 1 : (int)Math.Floor(Math.Log10(temp) + 1);
                    return digits >= 17;
                });

                permission.CleanUp();
            }
        }

        [HookMethod("OnPluginLoaded")]
        private void OnPluginLoaded(Plugin plugin)
        {
            // Call OnServerInitialized for hotloaded plugins
            if (serverInitialized)
            {
                plugin.CallHook("OnServerInitialized");
            }
        }

        [HookMethod("OnServerInitialized")]
        private void OnServerInitialized()
        {
            if (!serverInitialized)
            {
                Analytics.Collect();
                TheForestExtension.ServerConsole();
                SteamGameServer.SetGameTags("oxide,modded");

                serverInitialized = true;
            }
        }

        /// <summary>
        /// Called when the server is saved
        /// </summary>
        [HookMethod("OnServerSave")]
        private void OnServerSave()
        {
            Interface.Oxide.OnSave();
            Covalence.PlayerManager.SavePlayerData();
        }

        /// <summary>
        /// Called when the server is shutting down
        /// </summary>
        [HookMethod("OnServerShutdown")]
        private void OnServerShutdown()
        {
            Interface.Oxide.OnShutdown();
            Covalence.PlayerManager.SavePlayerData();
        }

        #endregion Core Hooks

        #region Command Handling

        /// <summary>
        /// Parses the specified chat command
        /// </summary>
        /// <param name="argstr"></param>
        /// <param name="cmd"></param>
        /// <param name="args"></param>
        private void ParseCommand(string argstr, out string cmd, out string[] args)
        {
            List<string> arglist = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool inlongarg = false;

            foreach (char c in argstr)
            {
                if (c == '"')
                {
                    if (inlongarg)
                    {
                        string arg = sb.ToString().Trim();

                        if (!string.IsNullOrEmpty(arg))
                        {
                            arglist.Add(arg);
                        }

                        sb = new StringBuilder();
                        inlongarg = false;
                    }
                    else
                    {
                        inlongarg = true;
                    }
                }
                else if (char.IsWhiteSpace(c) && !inlongarg)
                {
                    string arg = sb.ToString().Trim();

                    if (!string.IsNullOrEmpty(arg))
                    {
                        arglist.Add(arg);
                    }

                    sb = new StringBuilder();
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (sb.Length > 0)
            {
                string arg = sb.ToString().Trim();

                if (!string.IsNullOrEmpty(arg))
                {
                    arglist.Add(arg);
                }
            }

            if (arglist.Count == 0)
            {
                cmd = null;
                args = null;
                return;
            }

            cmd = arglist[0];
            arglist.RemoveAt(0);
            args = arglist.ToArray();
        }

        /// <summary>
        /// Called when a command was run from the server
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="command"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        [HookMethod("IOnServerCommand")]
        private object IOnServerCommand(BoltConnection connection, string command, string data)
        {
            if (command.Length != 0)
            {
                // Get the full command
                string cmd = command.TrimStart('/');
                string[] args = string.IsNullOrEmpty(data) ? new string[0] : data.Split();

                return Interface.Call("OnServerCommand", cmd, args) != null;
            }

            return null;
        }

        #endregion Command Handling

        #region Helpers

        /// <summary>
        /// Checks if the permission system has loaded, shows an error if it failed to load
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private bool PermissionsLoaded(IPlayer player)
        {
            if (!permission.IsLoaded)
            {
                player.Reply(string.Format(lang.GetMessage("PermissionsNotLoaded", this, player.Id), permission.LastException.Message));
                return false;
            }

            return true;
        }

        #endregion Helpers
    }
}
