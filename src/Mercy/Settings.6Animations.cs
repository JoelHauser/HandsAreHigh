using BepInEx.Configuration;
using UnityEngine;

namespace Mercy
{
    // F12 section "6. Animations": poses, gestures, kneeling, head and turning.
    // Bound in display order: common settings first, advanced ones (hidden until the menu's
    // Show advanced switch is on) after. Generated from one layout table in 0.28.0; edit here now.
    // Key names must never contain = \ " ' [ ] (BepInEx throws in Awake; scripts\pack.ps1 checks).
    // Renaming a key or moving it to another section needs a line in Settings.Layout.cs Moves.
    internal static partial class Settings
    {
        internal static ConfigEntry<bool> FaceCaptor;
        internal static ConfigEntry<float> Stance;
        internal static ConfigEntry<HandsStyle> HandsBehindHeadStyle;
        internal static ConfigEntry<float> OpenHands;
        internal static ConfigEntry<bool> KneelVariants;
        internal static ConfigEntry<bool> KneelBrace;
        internal static ConfigEntry<bool> KneelReposition;
        internal static ConfigEntry<bool> KneelFallbackCrouch;
        internal static ConfigEntry<bool> TurnSteps;
        internal static ConfigEntry<bool> Wiggle;
        internal static ConfigEntry<bool> Defiant;
        internal static ConfigEntry<bool> DemeanorEnabled;
        internal static ConfigEntry<bool> HeadMovement;
        internal static ConfigEntry<bool> KneelIdle;
        internal static ConfigEntry<bool> Flinch;
        internal static ConfigEntry<float> UpperArmRaise;
        internal static ConfigEntry<float> UpperArmForward;
        internal static ConfigEntry<float> ForearmForward;
        internal static ConfigEntry<float> ForearmInward;
        internal static ConfigEntry<float> ArmUpright;
        internal static ConfigEntry<float> ForearmTwist;
        internal static ConfigEntry<float> PoseStrength;
        internal static ConfigEntry<float> RaiseSeconds;
        internal static ConfigEntry<float> LowerSeconds;
        internal static ConfigEntry<float> EmergencyLowerSeconds;
        internal static ConfigEntry<float> DrawLowerSeconds;
        internal static ConfigEntry<float> TrembleDegrees;
        internal static ConfigEntry<float> TrembleSpeed;
        internal static ConfigEntry<float> FaceHysteresis;
        internal static ConfigEntry<float> TurnSpeed;
        internal static ConfigEntry<float> HandsOnHeadSeconds;
        internal static ConfigEntry<float> HandsOnHeadDown;
        internal static ConfigEntry<float> HandsOnHeadUp;
        internal static ConfigEntry<float> HandsOnHeadYaw;
        internal static ConfigEntry<float> WiggleChance;
        internal static ConfigEntry<float> WiggleMaxChance;
        internal static ConfigEntry<float> WiggleCooldown;
        internal static ConfigEntry<float> WiggleStrength;
        internal static ConfigEntry<int> WiggleSwings;
        internal static ConfigEntry<float> WiggleSwingSeconds;
        internal static ConfigEntry<float> WiggleDelayMin;
        internal static ConfigEntry<float> WiggleDelayMax;
        internal static ConfigEntry<float> WiggleWindow;
        internal static ConfigEntry<float> WiggleSampleGap;
        internal static ConfigEntry<float> WiggleClearance;
        internal static ConfigEntry<float> DefiantChance;
        internal static ConfigEntry<float> DefiantInfluence;
        internal static ConfigEntry<float> DefiantMaxChance;
        internal static ConfigEntry<float> DefiantCooldown;
        internal static ConfigEntry<float> DefiantDelayMin;
        internal static ConfigEntry<float> DefiantDelayMax;
        internal static ConfigEntry<float> DefiantWindow;
        internal static ConfigEntry<float> GestureBlend;
        internal static ConfigEntry<float> DemeanorStrength;
        internal static ConfigEntry<float> GlanceMin;
        internal static ConfigEntry<float> GlanceMax;
        internal static ConfigEntry<float> HeadMaxYaw;
        internal static ConfigEntry<float> HeadMaxDown;
        internal static ConfigEntry<float> HeadMaxUp;
        internal static ConfigEntry<float> HeadSpeed;
        internal static ConfigEntry<float> TorsoAssist;
        internal static ConfigEntry<float> KneelHeadYaw;
        internal static ConfigEntry<float> KneelTorsoYaw;
        internal static ConfigEntry<float> KneelIdleStrength;
        internal static ConfigEntry<float> FlinchStrength;
        internal static ConfigEntry<float> FlinchCooldown;
        internal static ConfigEntry<float> KneelDownSeconds;
        internal static ConfigEntry<float> KneelFrightenedSeconds;
        internal static ConfigEntry<float> KneelOneKneeSeconds;
        internal static ConfigEntry<float> KneelWeightSteady;
        internal static ConfigEntry<float> KneelWeightFrightened;
        internal static ConfigEntry<float> KneelWeightOneKnee;
        internal static ConfigEntry<float> KneelUpSeconds;
        internal static ConfigEntry<float> KneelDefensiveSeconds;
        internal static ConfigEntry<float> KneelQuickSeconds;
        internal static ConfigEntry<float> KneelBraceChance;
        internal static ConfigEntry<float> KneelStrength;
        internal static ConfigEntry<float> KneelLean;
        internal static ConfigEntry<float> KneelLeanScale;
        internal static ConfigEntry<float> KneeHeight;
        internal static ConfigEntry<float> KneelThighAngle;
        internal static ConfigEntry<float> KneeSpread;
        internal static ConfigEntry<float> KneelHipsForward;
        internal static ConfigEntry<float> KneelSettledWeight;
        internal static ConfigEntry<float> KneelTenseWeight;
        internal static ConfigEntry<float> KneelSettledThigh;
        internal static ConfigEntry<float> KneelTenseLean;
        internal static ConfigEntry<float> KneelVariantHold;
        internal static ConfigEntry<float> KneelMaxStep;
        internal static ConfigEntry<float> KneelMaxSlope;
        internal static ConfigEntry<float> KneelSpotRadius;
        internal static ConfigEntry<float> HandsBehindHeadStrength;
        internal static ConfigEntry<float> HandsBehindHead;
        internal static ConfigEntry<float> HandsAboveNeck;
        internal static ConfigEntry<float> ElbowsOut;
        internal static ConfigEntry<float> HelmetExtra;

        private static void BindAnimations(ConfigFile c)
        {
            FaceCaptor = c.Bind(S6, "Face the captor", true,
                Text("Turns to face whoever holds him.", false));
            Stance = c.Bind(S6, "Stance with hands up", 1f,
                Range("1 = standing, 0 = full crouch.", 0f, 1f, false));
            HandsBehindHeadStyle = c.Bind(S6, "Hands behind head", HandsStyle.Laced,
                Text("Laced: fingers interlaced. OneOverTheOther: one hand over the other. SideBySide: palms side by side, fingers meeting.", false));
            OpenHands = c.Bind(S6, "Open hands", 1f,
                Range("How far the fingers open while the hands are up. 0: the game's own gripping curl.", 0f, 1f, false));
            KneelVariants = c.Bind(S6, "Kneel variants", true,
                Text("Men kneel upright, settled back on their heels, or forward and tense, chosen from who they are and how they feel. Off: everyone kneels upright.", false));
            KneelBrace = c.Bind(S6, "Brace on the knee when rising", true,
                Text("The front hand pushes on the front thigh while he rises over that leg.", false));
            KneelReposition = c.Bind(S6, "Step to a spot with room", true,
                Text("No room to kneel where he stands: he looks round for a spot that fits and walks there with his hands up first.", false));
            KneelFallbackCrouch = c.Bind(S6, "Crouch when kneeling is impossible", false,
                Text("Where kneeling isn't possible: on, he crouches with his hands up; off, he stays standing. Either way the reason is logged.", false));
            TurnSteps = c.Bind(S6, "Turn with stepping feet", true,
                Text("Big turns use the game's standing-turn animation (the feet step). Off: always a pivot.", false));
            Wiggle = c.Bind(S6, "Wiggle", true,
                Text("Now and then a held bot leans left and right with his hands up (the friendly wiggle).", false));
            Defiant = c.Bind(S6, "Middle finger", true,
                Text("A held bot may flip you off once with his left hand while the right stays up. Only a gesture: no fight, no draw.", false));
            DemeanorEnabled = c.Bind(S6, "Demeanor styles", true,
                Text("Each held bot carries himself one of four ways (resigned, frightened, cautious, defiant), shaping how he kneels, looks, fidgets and speaks. Never changes what he decides.", false));
            HeadMovement = c.Bind(S6, "Head movement", true,
                Text("Held bots watch their captor and now and then glance at what they know about: their gun, where fire came from, a way out.", false));
            KneelIdle = c.Bind(S6, "Kneeling idle movement", true,
                Text("While he kneels: an occasional small weight shift and settling of the elbows.", false));
            Flinch = c.Bind(S6, "Flinch at near misses", true,
                Text("A round passing close to a held bot's head makes him duck and hunch for half a second.", false));
            UpperArmRaise = c.Bind(S6, "Arms: upper arm raise (deg)", 22f,
                Range("Elbow height: 0 = level with the shoulder, positive = above.", -40f, 80f, true));
            UpperArmForward = c.Bind(S6, "Arms: upper arm forward (deg)", 20f,
                Range("0 = straight out to the side, 90 = straight ahead.", -20f, 90f, true));
            ForearmForward = c.Bind(S6, "Arms: forearm forward (deg)", 4f,
                Range("0 = forearm straight up, positive tips the hands forward.", -30f, 80f, true));
            ForearmInward = c.Bind(S6, "Arms: forearm inward (deg)", 15f,
                Range("Positive tips the hands in towards the head.", -30f, 45f, true));
            ArmUpright = c.Bind(S6, "Arms: stay upright", 0.8f,
                Range("How much raised arms keep to true vertical when his upper body leans. 0: they lean with his chest; 1: fully upright.", 0f, 1f, true));
            ForearmTwist = c.Bind(S6, "Arms: forearm twist (deg)", 0f,
                Range("Turns the forearms about their length, to swing the palms forward.", -180f, 180f, true));
            PoseStrength = c.Bind(S6, "Arms: pose strength", 1f,
                Range("Below 1 lets breathing and sway show through the arms.", 0f, 1f, true));
            RaiseSeconds = c.Bind(S6, "Arms: raise time (s)", 0.35f,
                Range("How long the hands take to go up.", 0f, 2f, true));
            LowerSeconds = c.Bind(S6, "Arms: lower time (s)", 0.2f,
                Range("How long the hands take to come down.", 0f, 2f, true));
            EmergencyLowerSeconds = c.Bind(S6, "Arms: fast lower time (s)", 0.08f,
                Range("Shot, bolting, or scrambling for his gun.", 0f, 0.5f, true));
            DrawLowerSeconds = c.Bind(S6, "Arms: draw lower time (s)", 0.22f,
                Range("When he goes for a gun or knife he kept, the arms come down this fast while the draw starts.", 0f, 1f, true));
            TrembleDegrees = c.Bind(S6, "Arms: tremble (deg)", 0.8f,
                Range("0 turns it off.", 0f, 5f, true));
            TrembleSpeed = c.Bind(S6, "Arms: tremble speed", 6f,
                Range("How fast the tremble shakes.", 0f, 20f, true));
            FaceHysteresis = c.Bind(S6, "Turn to you after (deg)", 4f,
                Range("He re-aims his face only once you have moved this far around him, so he doesn't twitch with every step.", 0.5f, 30f, true));
            TurnSpeed = c.Bind(S6, "Turn speed (deg per s)", 110f,
                Range("How fast he turns on his feet.", 40f, 300f, true));
            HandsOnHeadSeconds = c.Bind(S6, "Hands on head: move time (s)", 0.9f,
                Range("Standing: how long the hands take from up to the back of his head, and back.", 0.3f, 3f, true));
            HandsOnHeadDown = c.Bind(S6, "Hands on head: head down max (deg)", 12f,
                Range("With his hands behind his head the neck can only bow a little; replaces Head: down max meanwhile.", 0f, 35f, true));
            HandsOnHeadUp = c.Bind(S6, "Hands on head: head up max (deg)", 16f,
                Range("Looking up with the hands on his head.", 0f, 30f, true));
            HandsOnHeadYaw = c.Bind(S6, "Hands on head: head turn max (deg)", 50f,
                Range("Standing with his hands on his head: the hands go round with the head.", 15f, 80f, true));
            WiggleChance = c.Bind(S6, "Wiggle: chance", 0.15f,
                Range("Per opportunity, before personality and nerve (cowards more, bold men less). Opportunities: soon after he settles, and when you look back at him after an opening.", 0f, 1f, true));
            WiggleMaxChance = c.Bind(S6, "Wiggle: max chance", 0.35f,
                Range("Cap on the final chance per opportunity.", 0f, 1f, true));
            WiggleCooldown = c.Bind(S6, "Wiggle: cooldown (s)", 90f,
                Range("Per bot, kept across recaptures.", 0f, 900f, true));
            WiggleStrength = c.Bind(S6, "Wiggle: strength", 0.6f,
                Range("Fraction of a full lean.", 0.1f, 1f, true));
            WiggleSwings = c.Bind(S6, "Wiggle: swings", 4,
                IntRange("Leans, alternating sides, then back to upright.", 2, 8, true));
            WiggleSwingSeconds = c.Bind(S6, "Wiggle: swing time (s)", 0.3f,
                Range("Per lean.", 0.15f, 1f, true));
            WiggleDelayMin = c.Bind(S6, "Wiggle: after settling min (s)", 0.8f,
                Range("Delay after his arms are fully up before the first opportunity, between min and max.", 0f, 10f, true));
            WiggleDelayMax = c.Bind(S6, "Wiggle: after settling max (s)", 2.5f,
                Range("Upper end of that delay.", 0f, 10f, true));
            WiggleWindow = c.Bind(S6, "Wiggle: window (s)", 4f,
                Range("A chosen wiggle waits at most this long for a suitable moment, else it is skipped.", 0.5f, 20f, true));
            WiggleSampleGap = c.Bind(S6, "Wiggle: sample gap (s)", 20f,
                Range("Least time between two wiggle rolls for one bot.", 1f, 300f, true));
            WiggleClearance = c.Bind(S6, "Wiggle: clearance (m)", 0.45f,
                Range("Free space needed on both sides; a wall closer than this skips the wiggle.", 0.1f, 1.5f, true));
            DefiantChance = c.Bind(S6, "Middle finger: chance", 0.05f,
                Range("Per surrender, rolled once after he settles, raised for bold personalities and high nerve, lowered for timid ones.", 0f, 1f, true));
            DefiantInfluence = c.Bind(S6, "Middle finger: personality influence", 1f,
                Range("0: every bot uses the plain chance; 1: full personality and nerve bias.", 0f, 1f, true));
            DefiantMaxChance = c.Bind(S6, "Middle finger: max chance", 0.15f,
                Range("Cap on the final per-surrender chance.", 0f, 1f, true));
            DefiantCooldown = c.Bind(S6, "Middle finger: cooldown (s)", 600f,
                Range("Per bot, kept across recaptures.", 0f, 3600f, true));
            DefiantDelayMin = c.Bind(S6, "Middle finger: after settling min (s)", 3f,
                Range("When the roll happens after his arms are fully up, between min and max.", 0f, 60f, true));
            DefiantDelayMax = c.Bind(S6, "Middle finger: after settling max (s)", 10f,
                Range("Upper end of that delay.", 0f, 60f, true));
            DefiantWindow = c.Bind(S6, "Middle finger: window (s)", 8f,
                Range("A chosen gesture waits at most this long for a suitable moment, else it is skipped.", 1f, 60f, true));
            GestureBlend = c.Bind(S6, "Gesture arm blend (s)", 0.15f,
                Range("How quickly the gesturing arm leaves and rejoins the hands-up pose.", 0.05f, 1f, true));
            DemeanorStrength = c.Bind(S6, "Demeanor: strength", 1f,
                Range("How different the styles look. 0: everyone neutral.", 0f, 1.5f, true));
            GlanceMin = c.Bind(S6, "Head: glance every min (s)", 3f,
                Range("Time between glances, between min and max, scaled by demeanor.", 1f, 30f, true));
            GlanceMax = c.Bind(S6, "Head: glance every max (s)", 9f,
                Range("Upper end of the glance interval.", 1f, 60f, true));
            HeadMaxYaw = c.Bind(S6, "Head: turn max (deg)", 70f,
                Range("Left/right from his body, standing.", 20f, 85f, true));
            HeadMaxDown = c.Bind(S6, "Head: down max (deg)", 35f,
                Range("How far he looks down.", 5f, 60f, true));
            HeadMaxUp = c.Bind(S6, "Head: up max (deg)", 20f,
                Range("How far he looks up (at most 12 with his hands behind his head).", 0f, 40f, true));
            HeadSpeed = c.Bind(S6, "Head: speed (deg per s)", 200f,
                Range("Top speed of a head turn.", 40f, 600f, true));
            TorsoAssist = c.Bind(S6, "Head: torso turn max (deg)", 15f,
                Range("Standing: a look far to the side turns his upper back by up to this much; never his hips.", 0f, 30f, true));
            KneelHeadYaw = c.Bind(S6, "Head: kneeling turn max (deg)", 55f,
                Range("Kneeling with his hands behind his head: how far the head turns from his chest.", 20f, 70f, true));
            KneelTorsoYaw = c.Bind(S6, "Head: kneeling torso turn max (deg)", 12f,
                Range("Kneeling: how far his upper back may turn from his hips to help a look.", 0f, 25f, true));
            KneelIdleStrength = c.Bind(S6, "Kneeling idle strength", 1f,
                Range("How big the kneeling idle movements are.", 0f, 2f, true));
            FlinchStrength = c.Bind(S6, "Flinch: strength", 1f,
                Range("How big the flinch is.", 0f, 1.5f, true));
            FlinchCooldown = c.Bind(S6, "Flinch: cooldown (s)", 0.7f,
                Range("Automatic fire can't restart it faster than this; each flinch leaves the next weaker for a while.", 0.2f, 5f, true));
            KneelDownSeconds = c.Bind(S6, "Kneel: steady time (s)", 2.3f,
                Range("Steady descent: one foot back, a knee down, the other beside it.", 0.8f, 5f, true));
            KneelFrightenedSeconds = c.Bind(S6, "Kneel: frightened time (s)", 1.3f,
                Range("Frightened descent: both knees down nearly together, hunched.", 0.6f, 4f, true));
            KneelOneKneeSeconds = c.Bind(S6, "Kneel: one knee first time (s)", 2.8f,
                Range("Down on one knee, a pause, then the other.", 1f, 6f, true));
            KneelWeightSteady = c.Bind(S6, "Kneel: steady weight", 1f,
                Range("Relative chance of each descent before personality, nerve, wounds and kit adjust it. 0 turns a style off.", 0f, 5f, true));
            KneelWeightFrightened = c.Bind(S6, "Kneel: frightened weight", 1f,
                Range("Relative chance of the frightened descent.", 0f, 5f, true));
            KneelWeightOneKnee = c.Bind(S6, "Kneel: one knee first weight", 0.8f,
                Range("Relative chance of the one-knee-first descent.", 0f, 5f, true));
            KneelUpSeconds = c.Bind(S6, "Kneel: stand up time (s)", 1.7f,
                Range("Compliant rise (ordered up, let go).", 0.6f, 5f, true));
            KneelDefensiveSeconds = c.Bind(S6, "Kneel: defensive stand time (s)", 0.9f,
                Range("Prompt, guarded rise when he draws or reacts to danger.", 0.4f, 3f, true));
            KneelQuickSeconds = c.Bind(S6, "Kneel: quick stand time (s)", 0.7f,
                Range("Escape rise (bolting, scrambling, shot): his fastest. Wounds and heavy kit slow every rise.", 0.4f, 2f, true));
            KneelBraceChance = c.Bind(S6, "Kneel: brace chance", 0.55f,
                Range("For a compliant rise; more for an escape and when wounded, less while drawing.", 0f, 1f, true));
            KneelStrength = c.Bind(S6, "Kneel: leg pose strength", 1f,
                Range("1: full kneel. Lower blends towards the game's crouch (for tuning).", 0f, 1f, true));
            KneelLean = c.Bind(S6, "Kneel: torso lean (deg)", 4f,
                Range("Forward lean of the back while kneeling.", -10f, 20f, true));
            KneelLeanScale = c.Bind(S6, "Kneel: transition lean scale", 1f,
                Range("Scales each style's forward lean in the middle of a transition.", 0f, 2f, true));
            KneeHeight = c.Bind(S6, "Kneel: knee height (m)", 0.09f,
                Range("Knee joint above the ground. Raise if knees sink, lower if they float.", 0.03f, 0.2f, true));
            KneelThighAngle = c.Bind(S6, "Kneel: thigh angle (deg)", 8f,
                Range("Thighs forward of vertical; more puts the knees further ahead of the hips.", 0f, 30f, true));
            KneeSpread = c.Bind(S6, "Kneel: knee spread (m)", 0.04f,
                Range("Knees this much wider than the hips, each side.", 0f, 0.2f, true));
            KneelHipsForward = c.Bind(S6, "Kneel: hips forward (m)", 0f,
                Range("Hips ahead (+) or behind (-) of where he stands.", -0.2f, 0.2f, true));
            KneelSettledWeight = c.Bind(S6, "Kneel: settled weight", 1f,
                Range("How often men settle back over their heels (upright = 1). 0: never.", 0f, 4f, true));
            KneelTenseWeight = c.Bind(S6, "Kneel: tense weight", 1f,
                Range("How often men kneel forward and tense (upright = 1). 0: never.", 0f, 4f, true));
            KneelSettledThigh = c.Bind(S6, "Kneel: settled thigh angle (deg)", 46f,
                Range("Thighs forward of vertical when settled back.", 20f, 55f, true));
            KneelTenseLean = c.Bind(S6, "Kneel: tense forward lean (deg)", 10f,
                Range("Extra forward lean of the tense kneel.", 0f, 20f, true));
            KneelVariantHold = c.Bind(S6, "Kneel: keep a variant at least (s)", 20f,
                Range("A man changes how he kneels at most this often, and only on a real change of mood.", 5f, 120f, true));
            KneelMaxStep = c.Bind(S6, "Kneel: max ground step (m)", 0.12f,
                Range("Ground under each knee and foot must be within this of where he stands, or he doesn't kneel.", 0.03f, 0.3f, true));
            KneelMaxSlope = c.Bind(S6, "Kneel: max slope (deg)", 20f,
                Range("Steeper ground under a knee or foot: he doesn't kneel.", 5f, 40f, true));
            KneelSpotRadius = c.Bind(S6, "Kneel: room search radius (m)", 2f,
                Range("How far from where he stands a spot with room is looked for.", 0.5f, 4f, true));
            HandsBehindHeadStrength = c.Bind(S6, "Hands behind head: strength", 1f,
                Range("0 keeps the hands-up arms while kneeling.", 0f, 1f, true));
            HandsBehindHead = c.Bind(S6, "Hands behind head: distance (m)", 0.11f,
                Range("How far behind the head the palms rest.", 0.04f, 0.25f, true));
            HandsAboveNeck = c.Bind(S6, "Hands behind head: height (m)", 0.08f,
                Range("Palms this far above the top of the neck.", 0f, 0.2f, true));
            ElbowsOut = c.Bind(S6, "Hands behind head: elbows out", 1f,
                Range("How far the elbows point out to the sides.", 0.3f, 1.5f, true));
            HelmetExtra = c.Bind(S6, "Hands behind head: helmet extra (m)", 0.035f,
                Range("Hands kept this much further from the head when he wears a helmet.", 0f, 0.1f, true));
        }
    }
}
