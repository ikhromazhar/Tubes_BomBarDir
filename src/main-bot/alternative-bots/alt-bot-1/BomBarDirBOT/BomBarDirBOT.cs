
using System;
using System.Collections.Generic;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class BomBarDirBOT : Bot
{
    private readonly Dictionary<int, int> enemyLastSeen = new Dictionary<int, int>();
private const int STALE_TICKS = 120;
    // ── Enemy tracking ─────────────────────────────────────
    private readonly Dictionary<int, ScannedBotEvent> enemies
        = new Dictionary<int, ScannedBotEvent>();
    private ScannedBotEvent lockedTarget = null;

    // ── Constants ──────────────────────────────────────────
    private const double WALL_MARGIN  = 120;
    private const double SAFE_DIST    = 280;

    // ── Strafe ─────────────────────────────────────────────
    private int strafeDir  = 1;
    private int strafeTick = 0;

    // ── Corner destination ─────────────────────────────────
    private double cornerX = -1;
    private double cornerY = -1;

    // ── Scan phase ─────────────────────────────────────────
    private int scanPhase = 6;

    static void Main(string[] args) => new BomBarDirBOT().Start();
    BomBarDirBOT() : base(BotInfo.FromFile("BomBarDirBOT.json")) { }

    // ══════════════════════════════════════════════════════
    //  RUN — satu keputusan per tick, Go() sekali di akhir
    // ══════════════════════════════════════════════════════
public override void Run()
{
    AdjustGunForBodyTurn   = true;
    AdjustRadarForBodyTurn = false;
    AdjustRadarForGunTurn  = false;

    while (IsRunning)
    {
        if (TurnNumber % 10 == 0)
            Console.WriteLine($"T={TurnNumber} E={Energy:F0} En={enemies.Count}/{EnemyCount} X={X:F0} Y={Y:F0}");

        PruneEnemies();

        if (EnemyCount == 0)
            DoIdle();
        else if (EnemyCount >= 2)
            DoEscape();
        // EnemyCount==1: tidak panggil DoDuel di sini, biarkan OnScannedBot yang handle

        if (EnemyCount == 1)
            SetTurnRadarRight(360); // sweep terus sampai ketemu

        Go();
    }
}

    // ══════════════════════════════════════════════════════
    //  ESCAPE MODE
    //  Greedy: pilih pojok terbaik tiap beberapa tick
    //  Bergerak TERUS ke sana tanpa berhenti
    // ══════════════════════════════════════════════════════
    private void DoEscape()
    {
        // Radar scan
        SetTurnRadarRight(45);

        if (enemies.Count == 0) return;

        // Pilih/update pojok setiap 20 tick supaya adaptif
        if (cornerX < 0 || TurnNumber % 20 == 0)
            PickBestCorner();

        double distToCorner = Dist(X, Y, cornerX, cornerY);

        if (distToCorner < 150)
        {
            // Sudah dekat pojok → strafe sepanjang tembok
            DoWallStrafe();
        }
        else
        {
            // Belum sampai → bergerak ke pojok
            MoveToward(cornerX, cornerY);
        }

        // Tembak kalau ada musuh dekat dan energy cukup
        DoFireIfSafe();
    }

    // ══════════════════════════════════════════════════════
    //  GREEDY: Pilih pojok terbaik
    //  Skor = total jarak ke semua musuh (makin jauh makin baik)
    //         + bonus jarak ke musuh terdekat
    //         - penalti path blocked
    // ══════════════════════════════════════════════════════
    private void PickBestCorner()
    {
        (double x, double y)[] corners = {
            (WALL_MARGIN,              WALL_MARGIN),
            (ArenaWidth - WALL_MARGIN, WALL_MARGIN),
            (WALL_MARGIN,              ArenaHeight - WALL_MARGIN),
            (ArenaWidth - WALL_MARGIN, ArenaHeight - WALL_MARGIN),
        };

        double bestScore = double.MinValue;
        (double x, double y) best = corners[0];

        foreach (var c in corners)
        {
            double totalDist = 0;
            double minDist   = double.MaxValue;

            foreach (var e in enemies.Values)
            {
                double d = Dist(c.x, c.y, e.X, e.Y);
                totalDist += d;
                if (d < minDist) minDist = d;
            }

            double pathPenalty = PathBlocked(c.x, c.y) ? 600 : 0;
            double score       = totalDist + minDist * 1.5 - pathPenalty;

            if (score > bestScore)
            {
                bestScore = score;
                best      = c;
            }
        }

        cornerX = best.x;
        cornerY = best.y;
    }

    // ══════════════════════════════════════════════════════
    //  MOVE TOWARD — gerak ke titik tujuan
    //  Kunci: SetTurnRight KECIL, SetForward BESAR
    //  Supaya tidak berhenti tiap tick
    // ══════════════════════════════════════════════════════
    private bool isTurning = false;

private void MoveToward(double tx, double ty)
{
    double angle = AngleTo(X, Y, tx, ty);
    double rel   = NormalizeRelativeAngle(angle - Direction);

    // Kalau mundur lebih efisien
    bool reverse = Math.Abs(rel) > 90;
    if (reverse) rel = NormalizeRelativeAngle(rel + 180);

    if (Math.Abs(rel) > 8)
    {
        // Fase 1: belok dulu, belum maju
        SetTurnRight(reverse ? -rel : rel);
        // Tetap set forward kecil supaya tidak diam total
        if (reverse) SetBack(30);
        else         SetForward(30);
    }
    else
    {
        // Fase 2: sudah lurus → GAS penuh
        if (reverse) SetBack(500);
        else         SetForward(500);
    }
}

    // ══════════════════════════════════════════════════════
    //  WALL STRAFE — gerak zigzag di dekat pojok
    //  Arah sejajar tembok terdekat
    // ══════════════════════════════════════════════════════
private void DoWallStrafe()
{
    strafeTick++;
    if (strafeTick > 18) { strafeDir = -strafeDir; strafeTick = 0; }

    double dL = X, dR = ArenaWidth - X;
    double dB = Y, dT = ArenaHeight - Y;
    double mn = Math.Min(Math.Min(dL, dR), Math.Min(dB, dT));

    double wallAngle = (mn == dL || mn == dR) ? 90 : 0;
    double rel       = NormalizeRelativeAngle(wallAngle - Direction);

    if (Math.Abs(rel) > 8)
    {
        SetTurnRight(rel);
        SetForward(strafeDir * 30); // pelan saat belok
    }
    else
    {
        SetForward(strafeDir * 500); // gas saat sudah lurus
    }
}
    // ══════════════════════════════════════════════════════
    //  FIRE IF SAFE — tembak hanya kalau worth
    // ══════════════════════════════════════════════════════
private void DoFireIfSafe()
{
    if (GunHeat != 0)  return;
    if (Energy < 30)   return;

    ScannedBotEvent target = NearestEnemy();
    if (target == null) return;

    double d = Dist(X, Y, target.X, target.Y);
    if (d > 400) return;

    double power = d < 150 ? 3 :
                   d < 300 ? 2 : 1;

    if (Energy <= power + 10) return;

    // Hitung sudut gun ke target
    double bulletSpeed = 20 - 3 * power;
    double ticks       = d / bulletSpeed;
    double fx = target.X + Math.Cos(target.Direction * Math.PI / 180.0) * target.Speed * ticks;
    double fy = target.Y + Math.Sin(target.Direction * Math.PI / 180.0) * target.Speed * ticks;
    fx = Math.Max(0, Math.Min(ArenaWidth,  fx));
    fy = Math.Max(0, Math.Min(ArenaHeight, fy));

    double gunRel = NormalizeRelativeAngle(AngleTo(X, Y, fx, fy) - GunDirection);

    // Arahkan gun dulu
    SetTurnGunRight(gunRel);

    // Tembak HANYA kalau gun sudah hampir lurus ke target (< 10 derajat)
    if (Math.Abs(gunRel) < 10 && GunHeat == 0)
        Fire(power);
}    // ══════════════════════════════════════════════════════
    //  DUEL MODE — 1 vs 1
    // ══════════════════════════════════════════════════════
// ══════════════════════════════════════════════════════
//  DUEL MODE — 1v1 (algoritma kode 2)
// ══════════════════════════════════════════════════════
private void DoDuel()
{
    ScannedBotEvent t = NearestEnemy();

    if (t == null)
    {
        SetTurnRadarRight(360);
        SetForward(200 * strafeDir);
        return;
    }

    // Radar: hitung manual tanpa API bearing
    double angleToTarget = DirectionTo(t.X, t.Y);
    double radarDiff = NormalizeRelativeAngle(angleToTarget - RadarDirection);
    SetTurnRadarRight(radarDiff + (radarDiff >= 0 ? 30 : -30));

    lockedTarget = t;
    double distance    = Dist(X, Y, t.X, t.Y);
    double bodyBearing = BearingTo(t.X, t.Y);

    // Strafe
    double perp1 = NormalizeRelativeAngle(bodyBearing + 90);
    double perp2 = NormalizeRelativeAngle(bodyBearing - 90);
    double perpTarget = (Math.Abs(perp1) < Math.Abs(perp2)) ? perp1 : perp2;
    SetTurnRight(perpTarget * 0.4);

    if (distance < 180)
        SetBack(200);
    else if (distance > 500)
        SetForward(300);
    else
    {
        strafeTick++;
        if (strafeTick > 20) { strafeDir = -strafeDir; strafeTick = 0; }
        SetForward(300 * strafeDir);
    }

    // Gun: juga manual
    double gunDiff = NormalizeRelativeAngle(angleToTarget - GunDirection);
    SetTurnGunRight(gunDiff);

    Console.WriteLine($"  >> radarDiff={radarDiff:F1} gunDiff={gunDiff:F1} GunHeat={GunHeat:F2}");

    if (GunHeat == 0 && Math.Abs(gunDiff) < 10)
        Fire(ChooseFirePower(distance, t.Energy));
}private double ChooseFirePower(double distance, double enemyEnergy)
{
    double power;

    if      (distance < 150) power = 3.0;
    else if (distance < 350) power = 2.2;
    else if (distance < 650) power = 1.4;
    else                     power = 0.8;

    // Hemat peluru kalau musuh hampir mati
    if (enemyEnergy < 12)
        power = Math.Min(power, enemyEnergy / 3.0 + 0.1);

    // Hemat energi sendiri kalau kritis
    if (Energy < 20)
        power = Math.Min(power, 1.0);

    return Math.Clamp(power, 0.1, 3.0);
}
    // ══════════════════════════════════════════════════════
    //  IDLE MODE
    // ══════════════════════════════════════════════════════
    private void DoIdle()
    {
        SetTurnRadarRight(360);
        strafeTick++;
        if (strafeTick > 20) { strafeDir = -strafeDir; strafeTick = 0; }
        SetTurnRight(strafeDir * 20);
        SetForward(200);
    }

    // ══════════════════════════════════════════════════════
    //  AIM AND FIRE — prediksi posisi musuh
    // ══════════════════════════════════════════════════════
    private void AimAndFire(ScannedBotEvent e, double power)
    {
        double bulletSpeed = 20 - 3 * power;
        double d           = Dist(X, Y, e.X, e.Y);
        double ticks       = d / bulletSpeed;

        double fx = e.X + Math.Cos(e.Direction * Math.PI / 180.0) * e.Speed * ticks;
        double fy = e.Y + Math.Sin(e.Direction * Math.PI / 180.0) * e.Speed * ticks;

        fx = Math.Max(0, Math.Min(ArenaWidth,  fx));
        fy = Math.Max(0, Math.Min(ArenaHeight, fy));

        double gunRel = NormalizeRelativeAngle(AngleTo(X, Y, fx, fy) - GunDirection);
        SetTurnGunRight(gunRel);
        if (GunHeat == 0) Fire(power);
    }

    // ══════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════
    private bool PathBlocked(double tx, double ty)
    {
        double at = AngleTo(X, Y, tx, ty);
        double dt = Dist(X, Y, tx, ty);

        foreach (var e in enemies.Values)
        {
            double de = Dist(X, Y, e.X, e.Y);
            if (de > dt) continue;
            double diff = Math.Abs(NormalizeRelativeAngle(AngleTo(X, Y, e.X, e.Y) - at));
            if (diff < 45) return true;
        }
        return false;
    }

    private ScannedBotEvent NearestEnemy()
    {
        ScannedBotEvent nearest = null;
        double minD = double.MaxValue;
        foreach (var e in enemies.Values)
        {
            double d = Dist(X, Y, e.X, e.Y);
            if (d < minD) { minD = d; nearest = e; }
        }
        return nearest;
    }

    private static double Dist(double x1, double y1, double x2, double y2)
        => Math.Sqrt((x2-x1)*(x2-x1) + (y2-y1)*(y2-y1));

    private static double AngleTo(double x1, double y1, double x2, double y2)
        => Math.Atan2(y2-y1, x2-x1) * 180.0 / Math.PI;

    // ══════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ══════════════════════════════════════════════════════
public override void OnScannedBot(ScannedBotEvent e)
{
    enemies[e.ScannedBotId] = e;
    enemyLastSeen[e.ScannedBotId] = TurnNumber;

    if (EnemyCount != 1) return;

    // Semua logika duel di sini — dipanggil tepat saat scan
    lockedTarget = e;
    double distance = Dist(X, Y, e.X, e.Y);

    // Lock radar langsung dari event ini
    double angleToTarget = DirectionTo(e.X, e.Y);
    double radarDiff = NormalizeRelativeAngle(angleToTarget - RadarDirection);
    SetTurnRadarRight(radarDiff + (radarDiff >= 0 ? 30 : -30));

    // Body strafe
    double bodyBearing = BearingTo(e.X, e.Y);
    double perp1 = NormalizeRelativeAngle(bodyBearing + 90);
    double perp2 = NormalizeRelativeAngle(bodyBearing - 90);
    double perpTarget = (Math.Abs(perp1) < Math.Abs(perp2)) ? perp1 : perp2;
    SetTurnRight(perpTarget * 0.4);

    if (distance < 180)
        SetBack(200);
    else if (distance > 500)
        SetForward(300);
    else
    {
        strafeTick++;
        if (strafeTick > 20) { strafeDir = -strafeDir; strafeTick = 0; }
        SetForward(300 * strafeDir);
    }

    // Gun & fire
    double gunDiff = NormalizeRelativeAngle(angleToTarget - GunDirection);
    SetTurnGunRight(gunDiff);

    if (GunHeat == 0 && Math.Abs(gunDiff) < 10)
        Fire(ChooseFirePower(distance, e.Energy));
}

public override void OnBotDeath(BotDeathEvent e)
{
    enemies.Remove(e.VictimId);
    enemyLastSeen.Remove(e.VictimId);
    if (lockedTarget != null && lockedTarget.ScannedBotId == e.VictimId)
        lockedTarget = null;
    cornerX = -1;
}
private void PruneEnemies()
{
    // Jangan prune kalau hasilnya enemies jadi kosong padahal masih ada musuh
    if (EnemyCount > 0 && enemies.Count <= EnemyCount)
        return;

    var stale = new List<int>();
    foreach (var kv in enemyLastSeen)
        if (TurnNumber - kv.Value > STALE_TICKS)
            stale.Add(kv.Key);
    foreach (var id in stale)
    {
        enemies.Remove(id);
        enemyLastSeen.Remove(id);
    }

    while (enemies.Count > EnemyCount && enemies.Count > 0)
    {
        int oldestId = -1;
        int oldestTick = int.MaxValue;
        foreach (var kv in enemyLastSeen)
            if (kv.Value < oldestTick) { oldestTick = kv.Value; oldestId = kv.Key; }
        if (oldestId >= 0)
        {
            enemies.Remove(oldestId);
            enemyLastSeen.Remove(oldestId);
        }
        else break;
    }
}


    public override void OnHitByBullet(HitByBulletEvent e)
    {
        // Balik arah strafe saat kena peluru — dodge otomatis
        strafeDir  = -strafeDir;
        strafeTick = 0;
    }

    public override void OnHitWall(HitWallEvent e)
    {
        strafeDir  = -strafeDir;
        strafeTick = 0;
        cornerX    = -1; // pilih ulang pojok
    }

    public override void OnHitBot(HitBotEvent e)
    {
        strafeDir  = -strafeDir;
        strafeTick = 0;
        cornerX    = -1; // pilih ulang pojok
    }
}