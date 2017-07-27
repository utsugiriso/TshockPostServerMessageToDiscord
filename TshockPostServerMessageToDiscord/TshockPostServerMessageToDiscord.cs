using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using Terraria.DataStructures;

using System.Net.Http;
using System.Net.Http.Headers;

using System.IO;

namespace TshockPostServerMessageToDiscord
{
    [ApiVersion(2, 1)]
    public class TshockPostServerMessageToDiscord : TerrariaPlugin
    {
        /// <summary>
        /// Gets the author(s) of this plugin
        /// </summary>
        public override string Author
        {
            get { return "utsugiriso"; }
        }

        /// <summary>
        /// Gets the description of this plugin.
        /// A short, one lined description that tells people what your plugin does.
        /// </summary>
        public override string Description
        {
            get { return "Post server message(login, chat, death, etc...) to discord channel."; }
        }

        /// <summary>
        /// Gets the name of this plugin.
        /// </summary>
        public override string Name
        {
            get { return "PostServerMessageToDiscord Plugin"; }
        }

        /// <summary>
        /// Gets the version of this plugin.
        /// </summary>
        public override Version Version
        {
            get { return new Version(0, 0, 0, 1); }
        }

        /// <summary>
        /// Initializes a new instance of the TestPlugin class.
        /// This is where you set the plugin's order and perfro other constructor logic
        /// </summary>
        public TshockPostServerMessageToDiscord(Main game) : base(game)
        {
        }

        /// <summary>
        /// Handles plugin initialization. 
        /// Fired when the server is started and the plugin is being loaded.
        /// You may register hooks, perform loading procedures etc here.
        /// </summary>
        public override void Initialize()
        {
            if (Configs == null)
                Configs = new Config();

            if (client == null)
            {
                client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", Configs.DiscordAppToken);
            }

            ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
            ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
            ServerApi.Hooks.NetGetData.Register(this, OnGetData);
            //ServerApi.Hooks.ServerChat.Register(this, OnServerChat);
            TShockAPI.Hooks.PlayerHooks.PlayerChat += OnPlayerChat;
        }

        /// <summary>
        /// Handles plugin disposal logic.
        /// *Supposed* to fire when the server shuts down.
        /// You should deregister hooks and free all resources here.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
                //ServerApi.Hooks.ServerChat.Deregister(this, OnServerChat);
                TShockAPI.Hooks.PlayerHooks.PlayerChat -= OnPlayerChat;
            }
            base.Dispose(disposing);
        }

        public static Config Configs = null;
        public class Config
        {
            public const string CHARACTER_NAME_FORMAT = "{character_name}";
            public const string MESSAGE_FORMAT = "{message}";
            public const string CURRENT_PLAYERS_FORMAT = "{current_players}";
            public const string SERVER_NAME_FORMAT = "{server_name}";
            public string CurrentPlayersMessageFormat = $"{CURRENT_PLAYERS_FORMAT} players now on {SERVER_NAME_FORMAT}.";
            public string LoginMessageFormat = $"{CHARACTER_NAME_FORMAT} has joined.";
            public string LogoutMessageFormat = $"{CHARACTER_NAME_FORMAT} has left.";
            public string ChatMessageFormat = $"<{CHARACTER_NAME_FORMAT}> {MESSAGE_FORMAT}";

            public string DiscordAppToken = null;
            public string DiscordCannelId = null;
            public string DiscordCreateMessageApiEndpoint = null;

            public Config()
            {
                string discordAppTokenPath = Path.Combine(TShock.SavePath, "discord_app_token.txt");
                DiscordAppToken = Read(discordAppTokenPath, "Bot MTk4NjIyNDgzNDcxOTI1MjQ4.Cl2FMQ.ZnCjm1XVW7vRze4b7Cq4se7kKWs");

                string discordCannelIdPath = Path.Combine(TShock.SavePath, "discord_channel_id.txt");
                DiscordCannelId = Read(discordCannelIdPath, "199737254929760256");

                DiscordCreateMessageApiEndpoint = $"https://discordapp.com/api/channels/{DiscordCannelId}/messages";

            }

            public string Read(string path, string defaultValue)
            {
                if (File.Exists(path))
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        return Read(fs);
                    }
                }
                else
                {
                    Write(path, defaultValue);
                    return null;
                }
            }

            public string Read(Stream stream)
            {
                using (var sr = new StreamReader(stream))
                {
                    var cf = sr.ReadToEnd();
                    return cf;
                }
            }

            public void Write(string path, string defaultValue)
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write))
                {
                    Write(fs, defaultValue);
                }
            }

            public void Write(Stream stream, string defaultValue)
            {
                using (var sw = new StreamWriter(stream))
                {
                    sw.Write(defaultValue);
                }
            }
        }

        private void OnServerJoin(JoinEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Who];
            if (player == null)
                return;
            string message = Configs.LoginMessageFormat.Replace(Config.CHARACTER_NAME_FORMAT, player.Name).Replace(Config.SERVER_NAME_FORMAT, TShock.Config.ServerName);

            List<string> players = TShock.Utils.GetPlayers(false);
            players.Add(player.Name);
            string currentPlayersMessage = Configs.CurrentPlayersMessageFormat.Replace(Config.CURRENT_PLAYERS_FORMAT, (TShock.Utils.ActivePlayers() + 1).ToString());
            message = $"{message}\n{currentPlayersMessage}\n{String.Join(", ", players.ToArray())}";

            //Console.WriteLine("OnServerJoin: {0}", message);
            PostMessageToDiscord(message);
        }

        private void OnServerLeave(LeaveEventArgs args)
        {
            if (args.Who >= TShock.Players.Length || args.Who < 0)
            {
                //Something not right has happened
                return;
            }

            TSPlayer tsplr = TShock.Players[args.Who];
            if (tsplr == null || String.IsNullOrEmpty(tsplr.Name))
            {
                return;
            }

            string message = Configs.LogoutMessageFormat.Replace(Config.CHARACTER_NAME_FORMAT, tsplr.Name).Replace(Config.SERVER_NAME_FORMAT, TShock.Config.ServerName);

            List<string> players = TShock.Utils.GetPlayers(false);
            players.Remove(tsplr.Name);
            int activePlayers = TShock.Utils.ActivePlayers();
            if (0 < activePlayers)
                activePlayers--;
            string currentPlayersMessage = Configs.CurrentPlayersMessageFormat.Replace(Config.CURRENT_PLAYERS_FORMAT, (activePlayers).ToString());
            message = $"{message}\n{currentPlayersMessage}\n{String.Join(", ", players.ToArray())}";

            //Console.WriteLine("OnServerLeave: {0}", message);
            PostMessageToDiscord(message);
        }


        private void OnPlayerChat(TShockAPI.Hooks.PlayerChatEventArgs args)
        {
            string message = Configs.ChatMessageFormat.Replace(Config.CHARACTER_NAME_FORMAT, args.Player.Name).Replace(Config.MESSAGE_FORMAT, args.RawText);
            //Console.WriteLine("OnPlayerChat: {0}", message);
            PostMessageToDiscord(message);
        }

        private void OnServerChat(ServerChatEventArgs args)
        {
            string message = args.Text;
            //Console.WriteLine("OnServerChat: {0}", message);
            PostMessageToDiscord(message);
        }

        private void OnGetData(GetDataEventArgs e)
        {
            PacketTypes type = e.MsgID;
            TSPlayer player = TShock.Players[e.Msg.whoAmI];

            if (player != null && type == PacketTypes.PlayerDeathV2)
            {
                string message = PlayerDeathReason.FromReader(e.Msg.reader).GetDeathText(player.Name).ToString();
                if (String.IsNullOrEmpty(message))
                {
                    message = PlayerDeathReason.LegacyDefault().GetDeathText(player.Name).ToString();
                }
                //Console.WriteLine("OnGetData: {0}", message);
                PostMessageToDiscord(message);
            }
        }

        private static HttpClient client = null;
        private void PostMessageToDiscord(string message)
        {
            //Console.WriteLine("start PostMessageToDiscord");
            if (!String.IsNullOrEmpty(Configs.DiscordAppToken) && !String.IsNullOrEmpty(Configs.DiscordCannelId))
            {
                PostMessageToDiscordRunAsync(message).Wait();
            }
            //Console.WriteLine("ends PostMessageToDiscord");
        }

        private async Task PostMessageToDiscordRunAsync(string message)
        {
            string content = await PostMessageToDiscordAsync(message);
        }

        private async Task<string> PostMessageToDiscordAsync(string message)
        {
            //Console.WriteLine("start PostMessageToDiscordAsync");
            HttpResponseMessage response = await client.PostAsync(Configs.DiscordCreateMessageApiEndpoint, new FormUrlEncodedContent(new Dictionary<string, string> { { "content", message } }));
            string content = await response.Content.ReadAsStringAsync();
            //Console.WriteLine("End PostMessageToDiscordAsync: {0}", content);
            return content;
        }
    }
}
