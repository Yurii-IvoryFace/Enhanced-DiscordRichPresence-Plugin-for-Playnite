namespace DiscordRichPresencePlugin.Enums
{
    public enum OpCode : int
    {
        Handshake = 0,
        Frame = 1,
        Close = 2,
        Ping = 3,
        Pong = 4
    }
}