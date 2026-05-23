using System;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class BomBarDirBOT : Bot
{
    private int targetId = -1;
    private double targetScore = -1;
    private int targetLastSeen = -1000;
    private int moveDir = 1;
    private int radarDir = 1;
    private int lastScanTurn = -1000;
    static void Main(string[] args)
    {
        new BomBarDirBOT().Start();
    }

    BomBarDirBOT() : base(BotInfo.FromFile("BomBarDirBOT.json")) { }

    public override void Run()
    {
            AdjustGunForBodyTurn = true;
            AdjustRadarForBodyTurn = true;
            AdjustRadarForGunTurn = true;
        while (IsRunning)
        {
            SetTurnLeft(20);
            SetForward(120*moveDir);
            SetTurnRadarLeft(45*radarDir);
            Go();
        }
    }
    private double CalculateTargetScore(double distance, double enemyEnergy, double gunBearing, double enemySpeed)
    {
        double distanceScore = Math.Max(0, 1200 - distance)/1200*45;
        double killScore = Math.Max(0, 100 - enemyEnergy)/100*35;
        double aimScore = Math.Max(0,20 - Math.Abs(gunBearing))/20*15;
        double speedPenalty = Math.Min(Math.Abs(enemySpeed), 8)*1.5;
        double dangerPenalty = distance < 100 ? 20 : 0;

        return distanceScore + killScore + aimScore - speedPenalty - dangerPenalty;
    }
    public double ChooseFirePower(double distance, double energyEnemy)
    {
        double power;
        if(distance < 150)
        {
            power = 3.0;
        }else if(distance < 350)
        {
            power = 2.2;
        }else if(distance < 650)
        {
            power = 1.4;
        }
        else
        {
            power = 0.8;
        }

        if(energyEnemy < 12)
        {
            power = Math.Min(power, energyEnemy/3.0+0.1);
        }
        if (Energy < 20)
        {
            power = Math.Min(power, 1.0);
        }
        return Math.Clamp(power, 0.1, 3.0);
    }
    public override void OnScannedBot(ScannedBotEvent e)
    {
        lastScanTurn = e.TurnNumber;
        double distance = DistanceTo(e.X, e.Y);
        double gunBearing = GunBearingTo(e.X, e.Y);
        double bodyBearing = BearingTo(e.X, e.Y);

        double scannedScore = CalculateTargetScore(distance, e.Energy, gunBearing, e.Speed);
        
        bool targetExpired = TurnNumber - targetLastSeen > 12;
        bool sameTarget = e.ScannedBotId == targetId;
        bool betterTarget = scannedScore > targetScore + 8;
        
        if(targetId == -1 || sameTarget || targetExpired || betterTarget)
        {
            targetId = e.ScannedBotId;
            targetScore = scannedScore;
            targetLastSeen = TurnNumber;
        }
        else
        {
            SetRescan();
            return;
        }
        SetTurnLeft(bodyBearing + 90 - (20 * moveDir));
        SetTurnGunLeft(gunBearing);
        SetTurnRadarLeft(RadarBearingTo(e.X, e.Y)*1.5);

        if (distance < 180)
        {
            moveDir *= -1;
            SetBack(100);
        }else if(distance > 500)
        {
            SetForward(100);
        }
        else
        {
            SetForward(100*moveDir);
        }
        if (GunHeat == 0 && Math.Abs(gunBearing) < 8)
        {
            double firePower = ChooseFirePower(distance, e.Energy);
            SetFire(firePower);
        }

        SetRescan();
    }

    public override void OnHitByBullet(HitByBulletEvent e)
    {
        moveDir *= -1;

        double bulletBearing = CalcBearing(e.Bullet.Direction);
        SetTurnLeft(90-bulletBearing);
        SetForward(140*moveDir);
        SetTurnRadarLeft(45*radarDir);
    }

    public override void OnHitBot(HitBotEvent e)
    {
        moveDir *= -1;
        double distance = DistanceTo(e.X, e.Y);
        double gunBearing = GunBearingTo(e.X, e.Y);
        SetTurnGunLeft(gunBearing);
        if(e.Energy < 16 && GunHeat == 0)
        {
            double firePower = ChooseFirePower(distance, e.Energy);
            SetFire(firePower);
        }
        SetBack(100);
        SetTurnLeft(60);
    }

    public override void OnHitWall(HitWallEvent e)
    {
        moveDir *= -1;
        radarDir *= -1;
        SetBack(120);
        SetTurnLeft(70);
    }
}
