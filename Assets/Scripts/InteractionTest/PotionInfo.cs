namespace WhoCastThat.Interactions
{
    /// <summary>
    /// Human-readable reference for every potion type: the name players see, a one-line
    /// rules summary, and how many are in the 40-card deck.
    ///
    /// This is the single place to reword the rules for players. Both the hover tooltip on a
    /// potion and any future rulebook/lobby UI read from here, so they can never drift apart.
    /// Purely presentational — no gameplay logic reads these strings.
    /// </summary>
    public static class PotionInfo
    {
        /// <summary>Display name shown at the top of the tooltip.</summary>
        public static string DisplayName(PotionType type)
        {
            switch (type)
            {
                case PotionType.Hex: return "Hex";
                case PotionType.Tribute: return "Tribute";
                case PotionType.Dispel: return "Dispel";
                case PotionType.Foresight: return "Foresight";
                case PotionType.Warp: return "Warp";
                case PotionType.Phase: return "Phase";
                case PotionType.Reflection: return "Reflection";
                case PotionType.Counterspell: return "Counterspell";
                case PotionType.Curse: return "Curse";
                default: return type.ToString();
            }
        }

        /// <summary>One-line summary of what casting this potion does.</summary>
        public static string Effect(PotionType type)
        {
            switch (type)
            {
                case PotionType.Hex:
                    return "End your turn. The next mage must take 2 turns in a row. Stacks.";
                case PotionType.Tribute:
                    return "The next mage hands you one of their potions.";
                case PotionType.Dispel:
                    return "Cancels the spell on the table. Playable out of turn.";
                case PotionType.Foresight:
                    return "Secretly view the top 3 potions in the cauldron.";
                case PotionType.Warp:
                    return "Reshuffle the cauldron.";
                case PotionType.Phase:
                    return "End your turn without drawing.";
                case PotionType.Reflection:
                    return "Copy the spell on the table — it strikes again for you. Out of turn.";
                case PotionType.Counterspell:
                    return "Survive a Curse. The Curse returns to the cauldron.";
                case PotionType.Curse:
                    return "Drawn, never cast. Answer with a Counterspell or you are destroyed.";
                default:
                    return "";
            }
        }

        /// <summary>When this potion may be played, for the tooltip's second line.</summary>
        public static string Timing(PotionType type)
        {
            switch (type)
            {
                case PotionType.Dispel:
                case PotionType.Reflection:
                    return "Any time a spell is waiting";
                case PotionType.Counterspell:
                    return "When you are cursed";
                case PotionType.Curse:
                    return "Cannot be cast";
                default:
                    return "On your turn";
            }
        }

        /// <summary>How many of this potion are in the 40-card deck.</summary>
        public static int CountInDeck(PotionType type)
        {
            switch (type)
            {
                case PotionType.Hex: return 5;
                case PotionType.Tribute: return 4;
                case PotionType.Dispel: return 4;
                case PotionType.Foresight: return 5;
                case PotionType.Warp: return 4;
                case PotionType.Phase: return 4;
                case PotionType.Reflection: return 4;
                case PotionType.Counterspell: return 6;
                case PotionType.Curse: return 4;
                default: return 0;
            }
        }

        /// <summary>
        /// The full hover tooltip: name, effect, and a small info table. Rich text, so the
        /// label needs a TMP component with rich text enabled (the default).
        /// </summary>
        public static string Tooltip(PotionType type)
        {
            return $"<b>{DisplayName(type)}</b>\n" +
                   $"{Effect(type)}\n" +
                   $"<size=80%><color=#B9A9D9>Play: {Timing(type)}   •   {CountInDeck(type)} in deck</color></size>";
        }

        /// <summary>Short label used for the draw reveal, where there is no room for the table.</summary>
        public static string DrawBanner(PotionType type)
        {
            return $"<b>{DisplayName(type)}</b>\n<size=75%>{Effect(type)}</size>";
        }
    }
}
