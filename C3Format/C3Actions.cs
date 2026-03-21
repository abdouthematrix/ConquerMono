namespace ConquerMono.C3Format;

/// <summary>
/// Conquer Online character animation action IDs.
/// The numeric value is the .c3 file name (e.g. action 110 → "110.c3").
/// </summary>
public static class C3Actions
{
    // ── Dance ─────────────────────────────────────────────────────────────────
    public const int Dance1 = 1;
    public const int Dance2 = 2;
    public const int Dance3 = 3;
    public const int Dance4 = 4;
    public const int Dance5 = 5;
    public const int Dance6 = 6;
    public const int Dance7 = 7;
    public const int Dance8 = 8;

    // ── Idle / Stand ──────────────────────────────────────────────────────────
    public const int Standby = 100;
    public const int Rest1 = 101;
    public const int Rest2 = 102;
    public const int Rest3 = 103;
    public const int Standby_I = 105;

    // ── Walk ──────────────────────────────────────────────────────────────────
    public const int WalkL = 110;
    public const int WalkR = 111;
    public const int WalkL_I = 115;
    public const int WalkR_I = 116;

    // ── Run ───────────────────────────────────────────────────────────────────
    public const int RunL = 120;
    public const int RunR = 121;
    public const int Transform = 122;
    public const int RunL_I = 125;
    public const int RunR_I = 126;

    // ── Jump ──────────────────────────────────────────────────────────────────
    public const int Jump = 130;
    public const int JumpBack = 131;
    public const int JumpRun = 132;
    public const int JumpAttack = 140;
    public const int JumpAttackEnd = 141;

    // ── Emotes ────────────────────────────────────────────────────────────────
    public const int Laugh = 140;
    public const int Guffaw = 151;
    public const int Fury = 160;
    public const int Sad = 150;
    public const int Excitement = 180;
    public const int SayHello = 190;
    public const int Salute = 160;
    public const int Genuflect = 170;

    // ── Sit / Lie ─────────────────────────────────────────────────────────────
    public const int Kneel = 220;
    public const int Cool = 230;
    public const int CoolPose = 231;
    public const int Swim = 240;
    public const int SitDown = 250;
    public const int SitDownStatic = 251;
    public const int Zazen = 260;
    public const int ZazenCool = 261;
    public const int Faint = 190;
    public const int Lie = 200;
    public const int PickUp = 262;

    // ── Work ──────────────────────────────────────────────────────────────────
    public const int Mine = 360;

    // ── Combat stance / hit / death ───────────────────────────────────────────
    public const int Alert = 340;
    public const int Alert_I = 305;
    public const int Dodge0 = 311;
    public const int Bruise0 = 321;
    public const int Bruise1 = 321;
    public const int Die0 = 330;
    public const int Body0 = 331;
    public const int Die1 = 332;
    public const int Body1 = 333;
    public const int Die2 = 334;
    public const int Body2 = 335;
    public const int Die3 = 336;
    public const int Body3 = 337;
    public const int DieFly = 340;
    public const int DieFlyEnd = 341;
    public const int WalkBack = 342;

    // ── Attack ────────────────────────────────────────────────────────────────
    public const int Attack0 = 350;
    public const int Attack1 = 351;
    public const int Attack2 = 352;

    // ── Skills / magic ────────────────────────────────────────────────────────
    public const int Relive = 400;
    public const int Puncture = 451;
    public const int AirStrike = 452;
    public const int Tornado = 453;
    public const int BodyShield = 460;
    public const int GodBelieve = 465;
    public const int Bump = 470;

    public const int Intone = 390;
    public const int IntoneDuration = 391;
    public const int IntoneLaunch = 392;

    // ── Fly ───────────────────────────────────────────────────────────────────
    public const int FlyStandby = 501;
    public const int FlyAlert = 502;
    public const int FlyMove = 510;
    public const int FlyAttack = 520;
    public const int FlyDown = 530;
    public const int FlyUp = 540;
    public const int FlyDie = 550;
    public const int FlyWound = 560;

    // ── Shooter ───────────────────────────────────────────────────────────────
    public const int JumpBackShoot = 610;
    public const int FastShoot = 620;
    public const int ChargeUpBegin = 630;
    public const int ChargeUpEnd = 631;

    // ── Team / TM skills ──────────────────────────────────────────────────────
    public const int TM_FastShoot = 800;
    public const int TM_DispersionShoot = 801;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the .c3 filename for a given action ID
    /// (e.g. <c>ToFileName(110)</c> → <c>"110.c3"</c>).
    /// </summary>
    public static string ToFileName(int actionId) => $"{actionId}.c3";

    /// <summary>
    /// Builds the full path to an action's .c3 file given the model directory.
    /// </summary>
    public static string ToPath(string modelDirectory, int actionId) =>
        System.IO.Path.Combine(modelDirectory, ToFileName(actionId));
}