using UnityEngine;

public class TestMonster : MonoBehaviour
{
    private string monsterName;
   
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.X))
        {
            Destroy(this.gameObject);
        }
    }

    private void OnDestroy()
    {
        EventBus.Instance.EventTrigger<TestMonster>("MonsterDead", this);
    }
}
