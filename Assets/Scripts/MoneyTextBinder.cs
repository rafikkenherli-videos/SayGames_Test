using TMPro;
using UnityEngine;

public class MoneyTextBinder : MonoBehaviour
{
    public MoneyBank bank;
    public TMP_Text text;

    void OnEnable()
    {
        if (bank) bank.onChanged.AddListener(UpdateText);
        UpdateText(bank ? bank.balance : 0);
    }
    void OnDisable()
    {
        if (bank) bank.onChanged.RemoveListener(UpdateText);
    }
    void UpdateText(int value)
    {
        if (text) text.SetText($" {value}");
    }
}
