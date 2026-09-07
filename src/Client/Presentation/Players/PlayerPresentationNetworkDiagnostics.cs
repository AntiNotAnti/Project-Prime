namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        internal bool ModDamageIndicatorActive
        {
            get
            {
                for (int i = 0; i < _damageIndicatorTimers.Length; i++)
                {
                    if (_damageIndicatorTimers[i] > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal (int Rows, float Height) ModScoreboardSize()
        {
            return (_scene.Match.ActivePlayers, GetScoreboardHeight());
        }
    }
}
