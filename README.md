
# Setup
Follow these steps to setup an instance of MemeweaverX3:
 1. Get a Discord bot token.
 2. Setup MySQL or MariaDB
 3. Setup a config.json
    1. Add the bot key under token.
    2. Set the MemeweaverDatabase connection string to the instance you wish to use.
    3. If execute the database migrations by issuing: `dotnet ef database update`
    4. Activate developer mode in the Discord client and get the ID of your test server. Put that ID into the `testGuild` setting.
 4. Ensure `opus` and `libsodium` are available to use.
    1. On windows, go to https://github.com/discord-net/Discord.Net/blob/dev/voice-natives/vnext_natives_win32_x64.zip and make sure those are in your PATH.
    2. On linux, TBD.
