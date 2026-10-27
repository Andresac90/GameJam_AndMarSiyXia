using System;
using UnityEngine;

public class TestPlayer : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        EventBus.Instance.AddEventListener<TestMonster>("MonsterDead", PlayerDoAfterMonsterDie);
    }

    private void PlayerDoAfterMonsterDie(TestMonster monster)
    {
        print("Monster is dead");
    }

    private void OnDestroy()
    {
        EventBus.Instance.RemoveEventListener<TestMonster>("MonsterDead", PlayerDoAfterMonsterDie);
    }
}
