using System;
using System.Collections.Generic;

using Verse;
using RimWorld;
using HarmonyLib;
using UnityEngine;
using System.Reflection;

namespace SocialEXPFromTrade
{
    class SocialEXPFromTrade : Mod
    {
        //private readonly bool isMadSkillsLoaded;

        public SocialEXPFromTrade( ModContentPack pack ) : base( pack )
        {
            GetSettings<Settings>();

            var harmony = new Harmony("krelinos.SocialEXPFromTrade");
            //harmony.PatchAll( Assembly.GetExecutingAssembly() );
            harmony.PatchAll( typeof( SocialEXPFromTrade ).Assembly );

            //isMadSkillsLoaded = ModLister.AnyModActiveNoSuffix( ["ratys.madskills", "cedaro.madskillsplus"] );
        }

        public override void DoSettingsWindowContents( Rect inRect )
        {
            Settings.DoSettingsWindowContents( inRect );
        }

        public override string SettingsCategory()
        {
            return "SocialEXPFromTrade.Title".Translate();
        }
    }

    [HarmonyPatch(typeof(TradeDeal))]
    [HarmonyPatch("TryExecute")]
    public class AddSocialExperiencePoints
    {
        static void Prefix( ref TradeDeal __instance, ref List<Tradeable> ___tradeables, ref object[] __state )
        {
            __state = new object[]
            {
                0f,     // Value of goods player is buying, in market value.
                0f,     // Value of goods player is selling, in actual selling value.
                false,  // Is pawn inspired for this trade?
                false   // Ignore this trade (because it was a Royal Tribute Collector trade)
            };

            if ( TradeSession.playerNegotiator.mindState.inspirationHandler.CurStateDef == InspirationDefOf.Inspired_Trade )
                __state[2] = true;

            if ( TradeSession.TradeCurrency == TradeCurrency.Favor )
            {
                //Log.Message( "Ignoring tribute trade. (Prefix)" );
                __state[3] = true;
            }
            else if ( TradeSession.TradeCurrency == TradeCurrency.Silver )
                foreach ( Tradeable aTradeable in ___tradeables )
                {
                    // Since silver currency is technically being traded over, it is getting
                    // counted for the experience gain, resulting in double the expected output.
                    if ( aTradeable.ThingDef.defName == "Silver" ) // Silver not Sliver
                        continue;

                    float xpToAdd = 0f;
                    if (aTradeable.ActionToDo == TradeAction.PlayerBuys)
                    {
                        switch ( Settings.BuyingDeterminer )
                        {
                            case Settings.ValueDeterminer.MarketValue:
                                xpToAdd = aTradeable.AnyThing.MarketValue;
                                break;
                            case Settings.ValueDeterminer.RelativeValue:
                                xpToAdd = aTradeable.GetPriceFor( TradeAction.PlayerBuys );
                                break;
                            case Settings.ValueDeterminer.HigherValue:
                                xpToAdd = Math.Max( aTradeable.AnyThing.MarketValue, aTradeable.GetPriceFor( TradeAction.PlayerBuys ) );
                                break;
                            case Settings.ValueDeterminer.LowerValue:
                                xpToAdd = Math.Min( aTradeable.AnyThing.MarketValue, aTradeable.GetPriceFor( TradeAction.PlayerBuys ) );
                                break;
                        }

                        //Log.Message( String.Format( "Player bought {0} {1} ({3}) at {2} silver each." ) );
                            //Math.Abs( aTradeable.CountToTransfer ), aTradeable.Label, xpToAdd, aTradeable.ThingDef ) );

                        xpToAdd *= Math.Abs(aTradeable.CountToTransfer);
                        __state[0] = (float)__state[0] + xpToAdd;
                    }
                    else if (aTradeable.ActionToDo == TradeAction.PlayerSells)
                    {
                        switch (Settings.SellingDeterminer)
                        {
                            case Settings.ValueDeterminer.MarketValue:
                                xpToAdd = aTradeable.AnyThing.MarketValue;
                                break;
                            case Settings.ValueDeterminer.RelativeValue:
                                xpToAdd = aTradeable.GetPriceFor( TradeAction.PlayerSells );
                                break;
                            case Settings.ValueDeterminer.HigherValue:
                                xpToAdd = Math.Max( aTradeable.AnyThing.MarketValue, aTradeable.GetPriceFor( TradeAction.PlayerSells ) );
                                break;
                            case Settings.ValueDeterminer.LowerValue:
                                xpToAdd = Math.Min( aTradeable.AnyThing.MarketValue, aTradeable.GetPriceFor( TradeAction.PlayerSells ) );
                                break;
                        }

                        //Log.Message( String.Format( "Player sold {0} {1} ({3}) at {2} silver each..",
                        //    Math.Abs( aTradeable.CountToTransfer ), aTradeable.ThingDef, xpToAdd, aTradeable.ThingDef ) );

                        xpToAdd *= Math.Abs( aTradeable.CountToTransfer );
                        __state[1] = (float)__state[1] + xpToAdd;
                    }
                }
        }
        static void Postfix( object[] __state, ref bool __result )
        {
            if (__result)
            {
                if ( (bool)__state[3] == false )
                {
                    Pawn playerNegotiator = TradeSession.playerNegotiator;
                    float xpRaw = (float)__state[0] * Settings.BuyingMultiplier + (float)__state[1] * Settings.SellingMultiplier;

                    if ( (bool)__state[2] == true )
                    {
                        xpRaw *= Settings.InspiredMultiplier;
                    }

                    float xpActual = 0;

                    // If I just do one "Learn" function, the skill gain acts like a skilltrainer and ignores the 4000 xp limit.
                    // Therefore, the experience gain needs to be manually separated into "before limit" and "after limit" groups.
                    if ( Settings.RespectDailyLearningLimit )
                    {
                        float pawnDailyLearnAmount =  playerNegotiator.skills.GetSkill(SkillDefOf.Social).xpSinceMidnight;
                        int pawnDailyLearnLimit = SkillRecord.MaxFullRateXpPerDay;
                        float pawnLearnRate = playerNegotiator.skills.GetSkill(SkillDefOf.Social).LearnRateFactor(false);

                        if ( pawnDailyLearnAmount < pawnDailyLearnLimit )
                        {
                            float learnDifference = pawnDailyLearnLimit - pawnDailyLearnAmount;
                            float xpRequiredToReachMaxLimit = learnDifference / pawnLearnRate;

                            float xpUnsaturated = Math.Min( xpRequiredToReachMaxLimit, xpRaw );

                            //playerNegotiator.skills.Learn( SkillDefOf.Social, xpUnsaturated, false );

                            xpRaw -= xpUnsaturated;
                            xpActual += xpUnsaturated * pawnLearnRate;

                            playerNegotiator.skills.Learn( SkillDefOf.Social, xpUnsaturated, false );

                            //Log.Message( String.Format( "learnDifference: {0} | xpToReachLimit: {1} | unsaturated: {2} | xpRaw: {3}",
                            //    learnDifference,
                            //    xpRequiredToReachMaxLimit,
                            //    xpUnsaturated,
                            //    xpRaw
                            //    ) );

                            if (xpRaw > 0)    // The rest gets added as "saturated" / over limit experience.
                            {
                                float xpSaturated = xpRaw / 5;
                                xpActual += xpSaturated * pawnLearnRate;

                                playerNegotiator.skills.Learn( SkillDefOf.Social, xpSaturated, false );
                            }
                        }
                        else
                        {
                            playerNegotiator.skills.Learn( SkillDefOf.Social, xpRaw, false );
                            xpActual += xpRaw * pawnLearnRate;
                        }
                    }
                    else
                    {
                        xpActual += xpRaw * playerNegotiator.skills.GetSkill( SkillDefOf.Social ).LearnRateFactor( false );

                        if ( playerNegotiator.skills.GetSkill( SkillDefOf.Social ).LearningSaturatedToday )
                        {
                            // Since the negotiator is already saturated on Social experience, I need to reverse the 20% penalty.
                            // And I can't just use the "direct" flag in the "Learn" function, because for some reason, it
                            // ignores Global Learning Factor (like the Slow/Fast Learner traits) and I don't like that.
                            xpActual *= 5;
                            playerNegotiator.skills.Learn( SkillDefOf.Social, xpRaw * 5, false );
                        }
                        else
                        {
                            playerNegotiator.skills.Learn( SkillDefOf.Social, xpRaw, false );
                        }
                    }

                    //int levelBeforeTrade = playerNegotiator.skills.GetSkill(SkillDefOf.Social).Level;

                    if ( PawnUtility.ShouldSendNotificationAbout( playerNegotiator ) && Settings.NotifyPlayer )
                    {
                        //var xpActual = xpRaw * playerNegotiator.skills.GetSkill(SkillDefOf.Social).LearnRateFactor();
                        Messages.Message( String.Format( "SocialEXPFromTrade.Notification".Translate(), playerNegotiator.LabelShort, Math.Round( xpActual, 0 ) ), playerNegotiator, MessageTypeDefOf.PositiveEvent, true );
                    }

                    //int levelAfterTrade = playerNegotiator.skills.GetSkill(SkillDefOf.Social).Level;

                    //Log.Message(String.Format("{0} has received {1} social experience from this trade. Is inspired? {2}", playerNegotiator.Name, playerXP, __state[2]));
                }
            }
        }
    }

    // Thank you Smart Turret Covering by denev for being simple and small enough for me to
    // reunderstand RimWorld mod settings.
    // https://steamcommunity.com/sharedfiles/filedetails/?id=2636621800
    public class Settings : ModSettings
    {
        public enum ValueDeterminer
        {
            MarketValue,
            RelativeValue,
            HigherValue,
            LowerValue
        }

        public static ValueDeterminer BuyingDeterminer = ValueDeterminer.MarketValue;
        public static ValueDeterminer SellingDeterminer = ValueDeterminer.RelativeValue;

        public static float InspiredMultiplier = 1f;
        public static float GlobalMultiplier = 1f;
        public static float BuyingMultiplier = 1f;
        public static float SellingMultiplier = 1f;

        public static bool RespectDailyLearningLimit = true;
        public static bool NotifyPlayer = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look( ref BuyingDeterminer, "SocialEXPFromTrade_BuyingDeterminer", ValueDeterminer.MarketValue );
            Scribe_Values.Look( ref SellingDeterminer, "SocialEXPFromTrade_SellingDeterminer", ValueDeterminer.MarketValue );
            Scribe_Values.Look( ref InspiredMultiplier, "SocialEXPFromTrade_InspiredMultiplier", 1f );
            Scribe_Values.Look( ref GlobalMultiplier, "SocialEXPFromTrade_GlobalMultiplier", 1f );  // Deprecated, but keeping so people's existing global multiplier translate to buy and sell multipliers.
            Scribe_Values.Look( ref BuyingMultiplier, "SocialEXPFromTrade_BuyingMultiplier", GlobalMultiplier );
            Scribe_Values.Look( ref SellingMultiplier, "SocialEXPFromTrade_SellingMultiplier", GlobalMultiplier );
            Scribe_Values.Look( ref RespectDailyLearningLimit, "SocialEXPFromTrade_RespectDailyLearningLimit", true );
            Scribe_Values.Look( ref NotifyPlayer, "SocialEXPFromTrade_NotifyPlayer", true );
        }

        public static void DoSettingsWindowContents( Rect inRect )
        {
            var listing = new Listing_Standard();

            listing.Begin( inRect );

            ////// BUYING

            Text.Font = GameFont.Small;
            listing.Label( "SocialEXPFromTrade.BuyingDeterminer".Translate() );
            if (listing.RadioButton( "　" + "SocialEXPFromTrade.MarketValue".Translate() + " " + "SocialEXPFromTrade.Default".Translate(), BuyingDeterminer == ValueDeterminer.MarketValue ))
                BuyingDeterminer = ValueDeterminer.MarketValue;

            Text.Font = GameFont.Tiny;
            listing.Label( "　　　<color=grey>" + "SocialEXPFromTrade.MarketValueDescription".Translate() + "</color>" );

            Text.Font = GameFont.Small;
            if (listing.RadioButton( "　" + "SocialEXPFromTrade.RelativeValue".Translate(), BuyingDeterminer == ValueDeterminer.RelativeValue ))
                BuyingDeterminer = ValueDeterminer.RelativeValue;

            Text.Font = GameFont.Tiny;
            listing.Label( "　　　<color=grey>" + "SocialEXPFromTrade.RelativeValueDescription".Translate() + "</color>" );

            Text.Font = GameFont.Small;
            if (listing.RadioButton( "　" + "SocialEXPFromTrade.HigherValue".Translate(), BuyingDeterminer == ValueDeterminer.HigherValue ))
                BuyingDeterminer = ValueDeterminer.HigherValue;
            //listing.Label( "" );

            if (listing.RadioButton( "　" + "SocialEXPFromTrade.LowerValue".Translate(), BuyingDeterminer == ValueDeterminer.LowerValue ))
                BuyingDeterminer = ValueDeterminer.LowerValue;

            listing.Gap( 12 );

            ////// SELLING

            Text.Font = GameFont.Small;
            listing.Label( "SocialEXPFromTrade.SellingDeterminer".Translate() );
            if (listing.RadioButton( "　" + "SocialEXPFromTrade.MarketValue".Translate() + " " + "SocialEXPFromTrade.Default".Translate(), SellingDeterminer == ValueDeterminer.MarketValue ))
                SellingDeterminer = ValueDeterminer.MarketValue;

            Text.Font = GameFont.Tiny;
            listing.Label( "　　　<color=grey>" + "SocialEXPFromTrade.MarketValueDescription".Translate() + "</color>" );

            Text.Font = GameFont.Small;
            if (listing.RadioButton( "　" + "SocialEXPFromTrade.RelativeValue".Translate(), SellingDeterminer == ValueDeterminer.RelativeValue ))
                SellingDeterminer = ValueDeterminer.RelativeValue;

            Text.Font = GameFont.Tiny;
            listing.Label( "　　　<color=grey>" + "SocialEXPFromTrade.RelativeValueDescription".Translate() + "</color>" );

            Text.Font = GameFont.Small;
            if (listing.RadioButton( "　" + "SocialEXPFromTrade.HigherValue".Translate(), SellingDeterminer == ValueDeterminer.HigherValue ))
                SellingDeterminer = ValueDeterminer.HigherValue;
            //listing.Label( "" );

            if (listing.RadioButton( "　" + "SocialEXPFromTrade.LowerValue".Translate(), SellingDeterminer == ValueDeterminer.LowerValue ))
                SellingDeterminer = ValueDeterminer.LowerValue;

            listing.Gap( 12 );

            ////// MULTIPLIERS

            Text.Font = GameFont.Small;

            listing.Label( String.Format( "SocialEXPFromTrade.InspirationMultiplier".Translate(), InspiredMultiplier ) );
            InspiredMultiplier = listing.Slider( (float)Math.Round( InspiredMultiplier, 1 ), 0f, 10f );

            listing.Label( String.Format( "SocialEXPFromTrade.BuyingMultiplier".Translate(), BuyingMultiplier ) );
            BuyingMultiplier = listing.Slider( (float)Math.Round( BuyingMultiplier, 1 ), 0f, 10f );
            
            listing.Label( String.Format( "SocialEXPFromTrade.SellingMultiplier".Translate(), SellingMultiplier ) );
            SellingMultiplier = listing.Slider( (float)Math.Round( SellingMultiplier, 1 ), 0f, 10f );

            listing.Gap( 8 );

            listing.CheckboxLabeled( "SocialEXPFromTrade.RespectDailyLearnLimit".Translate(), ref RespectDailyLearningLimit );
            listing.CheckboxLabeled( "SocialEXPFromTrade.NotifyPlayer".Translate(), ref NotifyPlayer );
            listing.End();
        }
    }
}
