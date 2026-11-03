public class DailyStoryProgress
{
    public const int FirewoodCost = 3;
    public const int FurnitureCost = 3;
    public const int RequiredTrees = 3;
    public const int RequiredStoredWood = 9;
    public int TreesCut { get; private set; }
    public int StoredWood { get; private set; }
    public int DepositedWood { get; private set; }
    public int DayTwoWoodDeposited { get; private set; }
    public int WorkbenchWood { get; private set; }
    public void DeliverWorkbenchWood()
    {
        if (Day == 2 && !FurnitureCrafted && WorkbenchWood < FurnitureCost) WorkbenchWood++;
    }
    public bool IsNight { get; private set; }
    public int Day { get; private set; } = 1;
    public bool FireLit { get; private set; }
    public int HearthWood { get; private set; }
    public bool AxeReturned { get; private set; }
    public void SetAxeReturned(bool returned)
    {
        AxeReturned = returned;
        CheckNight();
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
        CheckNight();
    }
    public bool FurnitureCrafted { get; private set; }
    public bool CanSleepToNextDay => Day == 1 && FireLit;

    public bool LightFire(int availableWood)
    {
        if (Day != 1 || !IsNight || FireLit || HearthWood < FirewoodCost) return false;
        HearthWood -= FirewoodCost;
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
        if (Day == 1 && TreesCut >= RequiredTrees && DepositedWood >= RequiredStoredWood &&
            AxeReturned && HearthWood >= FirewoodCost)
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
        if (Day != 2 || FurnitureCrafted || WorkbenchWood < FurnitureCost) return false;
        WorkbenchWood -= FurnitureCost;
        FurnitureCrafted = true;
        return true;
    }
}
