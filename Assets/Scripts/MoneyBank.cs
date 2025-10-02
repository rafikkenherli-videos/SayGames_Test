using UnityEngine;
using UnityEngine.Events;

public class MoneyBank : MonoBehaviour
{
    public int balance;
    public UnityEvent<int> onChanged;

    public void Add(int amount)
    {
        balance += amount;
        onChanged?.Invoke(balance);
    }
}
