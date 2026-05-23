using System;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class BomBarDirBOT : Bot
{
    static void Main(string[] args) => new BomBarDirBOT().Start();
    BomBarDirBOT() : base(BotInfo.FromFile("BomBarDirBOT.json")) { }

    private int?   _targetId = null;
    private int    _moveDir  = 1;
    private int    _turnDir  = 1;
    private int    _tick     = 0;

    public override void Run()
    {
        BodyColor   = Color.FromArgb(0xCC, 0x00, 0x00);
        TurretColor = Color.FromArgb(0xFF, 0x33, 0x33);
        RadarColor  = Color.FromArgb(0xFF, 0xFF, 0x00);
        BulletColor = Color.FromArgb(0xFF, 0x88, 0x00);
        ScanColor   = Color.FromArgb(0xFF, 0xCC, 0x00);
        TracksColor = Color.FromArgb(0x88, 0x00, 0x00);
        GunColor    = Color.FromArgb(0xFF, 0x66, 0x00);

        while (IsRunning)
        {
            // Selama belum ada target, radar putar cari musuh
            if (_targetId == null)
                TurnRadarRight(45);
            else
                Forward(0); // dummy biar loop jalan
        }
    }

    public override void OnScannedBot(ScannedBotEvent e)
    {
        // Kalau belum punya target, lock bot pertama yang keliatan
        if (_targetId == null)
            _targetId = e.ScannedBotId;

        // Abaikan kalau bukan target kita
        if (e.ScannedBotId != _targetId) return;

        _tick++;

        // 1. Kunci radar tepat ke target, overshoot dikit biar tidak lepas
        double radarTurn = NormRel(
            Math.Atan2(e.X - X, e.Y - Y) * 180.0 / Math.PI - RadarDirection
        );
        TurnRadarRight(radarTurn * 1.2);

        // 2. Arahkan gun ke target
        double gunTurn = NormRel(
            Math.Atan2(e.X - X, e.Y - Y) * 180.0 / Math.PI - GunDirection
        );
        TurnGunRight(gunTurn);

        // 3. Tembak
        Fire(1.0);

        // 4. Gerak zigzag hindari dinding
        if (X < 80 || X > ArenaWidth - 80 || Y < 80 || Y > ArenaHeight - 80)
        {
            double ang = NormRel(
                Math.Atan2(ArenaWidth / 2.0 - X, ArenaHeight / 2.0 - Y) * 180.0 / Math.PI - Direction
            );
            TurnLeft(ang);
            Forward(120);
        }
        else
        {
            if (_tick % 6 == 0) { TurnRight(30 * _turnDir); _turnDir = -_turnDir; }
            if (_tick % 10 == 0) _moveDir = -_moveDir;
            if (_moveDir > 0) Forward(70);
            else Back(50);
        }
    }

    public override void OnBotDeath(BotDeathEvent e)
    {
        if (e.VictimId != _targetId) return;
        // Target mati → reset, Run() akan scan lagi
        _targetId = null;
        _tick = 0;
    }

    public override void OnHitByBullet(HitByBulletEvent e)
    {
        TurnLeft(90 - CalcBearing(e.Bullet.Direction));
        _turnDir = -_turnDir;
    }

    public override void OnHitBot(HitBotEvent e) { Back(60); _moveDir = -_moveDir; }
    public override void OnHitWall(HitWallEvent e) { Back(80); TurnRight(90); }

    private static double NormRel(double a)
    {
        a %= 360;
        if (a >  180) a -= 360;
        if (a < -180) a += 360;
        return a;
    }
}
