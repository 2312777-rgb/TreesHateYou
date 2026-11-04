public class DailyStoryProgress
{
    public const int FirewoodCost = 2;
    public const int RequiredTrees = 3;
    public const int RequiredStoredWood = 9;
    public int TreesCut { get; private set; }
    public int StoredWood { get; private set; }
    public int DepositedWood { get; private set; }
    public bool IsNight { get; private set; }
    public int Day { get; private set; } = 1;
    public bool FireLit { get; private set; }
    public int HearthWood { get; private set; }
    public bool AxeReturned { get; private set; }
    public void SetAxeReturned(bool returned)
    {
        AxeReturned = returned;
    }

    public bool WithdrawWood()
    {
        if (StoredWood <= 0) return false;
        StoredWood--;
        return true;
    }

    public void LoadHearthWood()
    {
        if (!FireLit && HearthWood < FirewoodCost) HearthWood++;
    }
    public bool CanSleepToNextDay => Day == 1 && FireLit && IsNight;

    public bool LightFire(int availableWood)
    {
        if (FireLit || HearthWood < FirewoodCost) return false;
        if (Day == 1 && (TreesCut < RequiredTrees || DepositedWood < RequiredStoredWood || !AxeReturned))
            return false;
        HearthWood -= FirewoodCost;
        FireLit = true;
        return true;
    }

    public void FinishSunset()
    {
        if (Day == 1 && FireLit) IsNight = true;
    }

    public void RecordTreeCut()
    {
        if (Day != 1) return;
        TreesCut = System.Math.Min(TreesCut + 1, RequiredTrees);
    }

    public bool DepositWood(int amount)
    {
        if (amount <= 0) return false;
        StoredWood += amount;
        DepositedWood += amount;
        return true;
    }

    public bool AdvanceDay()
    {
        if (!CanSleepToNextDay) return false;
        Day = 2;
        FireLit = false;
        IsNight = false;
        return true;
    }

}
