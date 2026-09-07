using MphRead.Mods.Network;
using MphRead.Hud;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        /// <summary>
        /// Column centres in the HUD's 256-wide space. The stock two sit at
        /// 160 and 215, which leaves no room for a third: "deaths" is six
        /// characters at eight pixels each and ends at 239. In a networked
        /// match the two of them move left to make room, and offline nothing
        /// moves at all.
        /// </summary>
        internal float ModScoreColumn1 => (NetSession.Active || AuthoritativePlay.Active) ? 145 : 160;
        internal float ModScoreColumn2 => (NetSession.Active || AuthoritativePlay.Active) ? 193 : 215;

        private const float _pingColumnX = 236;
        public void ModDrawPingHeader(float posY)
        {
            if (!NetSession.Active && !AuthoritativePlay.Active)
            {
                return;
            }

            DrawText2D(_pingColumnX, posY, Align.Center, 0, "ping", new ColorRgba(0x3FEF), fontSpacing: 8);
        }

        public void ModDrawPingRow(float posY, ColorRgba rowColor, int slot)
        {
            if ((!NetSession.Active && !AuthoritativePlay.Active) || slot < 0 || slot >= PlayerEntity.SlotCapacity)
            {
                return;
            }

            int ping = NetSession.SlotPing[slot];
            if (AuthoritativePlay.Current is { } play)
            {
                ping = 0;
                foreach (NetRosterEntry entry in play.Client.Roster)
                {
                    if (entry.Slot == slot)
                    {
                        ping = entry.PingMs;
                        break;
                    }
                }
            }

            // Zero means the server has not timed this peer yet -- a dash says
            // that, where "0" would read as a perfect connection.
            string text = ping <= 0 ? "--" : (ping > 999 ? "999" : ping.ToString());
            DrawText2D(_pingColumnX, posY, Align.Center, 0, text, PingColor(ping), fontSpacing: 8);
        }

        public static ColorRgba PingColor(int ping)
        {
            if (ping <= 0)
            {
                return new ColorRgba(120, 120, 140, 255);
            }

            if (ping < 80)
            {
                return new ColorRgba(110, 231, 135, 255);
            }

            if (ping < 160)
            {
                return new ColorRgba(255, 200, 80, 255);
            }

            return new ColorRgba(255, 110, 110, 255);
        }
    }
}
