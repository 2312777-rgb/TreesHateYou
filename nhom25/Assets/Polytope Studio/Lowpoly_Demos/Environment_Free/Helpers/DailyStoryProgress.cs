public class DailyStoryProgress
{
    public const int FirewoodCost = 3;
    public const int FurnitureCost = 5;
    public const int RequiredTrees = 3;
    public const int RequiredStoredWood = 9;
    public int TreesCut { get; private set; }
    public int StoredWood { get; private set; }
    public int DepositedWood { get; private set; }
    public int DayTwoWoodDeposited { get; private set; }
    public bool IsNight { get; private set; }
    public int Day { get; private set; } = 1;
    public bool FireLit { get; private set; }
    public bool FurnitureCrafted { get; private set; }
    public bool CanSleepToNextDay => Day == 1 && FireLit;

    public bool LightFire(int availableWood)
    {
        if (Day != 1 || !IsNight || FireLit || StoredWood < FirewoodCost) return false;
        StoredWood -= FirewoodCost;
        FireLit = true;
        return true;
    }

    public void RecordTreeCut()
    {
        if (Day != 1) return;
        TreesCut = System.Math.Min(TreesCut + 1, RequiredTrees);
        CheckNight();
    }

    public bool DepositWood(int amount)
    {
        if (amount <= 0) return false;
        StoredWood += amount;
        DepositedWood += amount;
        if (Day == 2) DayTwoWoodDeposited += amount;
        CheckNight();
        return true;
    }

    private void CheckNight()
    {
        if (Day == 1 && TreesCut >= RequiredTrees && DepositedWood >= RequiredStoredWood)
            IsNight = true;
    }

    public bool AdvanceDay()
    {
        if (!CanSleepToNextDay) return false;
        Day = 2;
        IsNight = false;
        return true;
    }

    public bool CraftFurniture(int availableWood)
    {
        if (Day != 2 || FurnitureCrafted || availableWood < FurnitureCost ||
            StoredWood < FurnitureCost || DayTwoWoodDeposited < FurnitureCost) return false;
        StoredWood -= FurnitureCost;
        FurnitureCrafted = true;
        return true;
    }
}
