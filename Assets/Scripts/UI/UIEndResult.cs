using System;
using System.Collections.Generic;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;   // === MODIFIED: must be inside #if UNITY_EDITOR or player builds fail. ===
#endif
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;   // === ADDED: needed for SceneManager.LoadScene. ===
using UnityEngine.UI;

public class UIEndResult : MonoBehaviour
{
    private Dictionary<string, UIBehaviour> uiElements = new Dictionary<string, UIBehaviour>();

    [SerializeField] private GameObject uiEndResult;

    public TextMeshProUGUI candyReceived;
    public TextMeshProUGUI monsterKilled;

    void Start()
    {
        FindUIElements<Button>(uiElements);

        // === MODIFIED: null-safe button lookup, so a missing button doesn't NRE the whole Start. ===
        if (uiElements.TryGetValue("BtnRestart", out var restart) && restart is Button restartBtn)
            restartBtn.onClick.AddListener(OnRestartButtonClicked);
        else
            Debug.LogWarning("UIEndResult: 'BtnRestart' button not found.");

        if (uiElements.TryGetValue("BtnExit", out var exit) && exit is Button exitBtn)
            exitBtn.onClick.AddListener(OnExitButtonClicked);
        else
            Debug.LogWarning("UIEndResult: 'BtnExit' button not found.");

        // === MODIFIED: pull current values from GameLoop instead of hardcoding 0,
        // so the labels stay correct regardless of Start() ordering. ===
        GameLoop loop = FindAnyObjectByType<GameLoop>();
        candyReceived.text = (loop != null ? loop.Candy : 0).ToString();
        monsterKilled.text = (loop != null ? loop.MonsterKills : 0).ToString();

        uiEndResult.SetActive(false);

        EventBus.Instance.AddEventListener("EndResult", ShowResult);
        EventBus.Instance.AddEventListener<int>("CandyReceived", ShowCandyNum);
        EventBus.Instance.AddEventListener<int>("MonsterKilled", ShowMonsterNum);
    }

    // === VERIFIED: correct target label (monsterKilled). ===
    private void ShowMonsterNum(int num)
    {
        monsterKilled.text = num.ToString();
    }

    // === VERIFIED: correct target label (candyReceived). ===
    private void ShowCandyNum(int num)
    {
        candyReceived.text = num.ToString();
    }

    private void OnRestartButtonClicked()
    {
        // === ADDED: actually reload the scene and restore time scale. ===
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnExitButtonClicked()
    {
#if UNITY_EDITOR
        EditorApplication.ExitPlaymode();
#endif
        Application.Quit();
    }

    private void FindUIElements<T>(Dictionary<string, UIBehaviour> uiElements) where T : UIBehaviour
    {
        T[] buttons = GetComponentsInChildren<T>(true);
        foreach (T button in buttons)
        {
            // === MODIFIED: skip nulls just in case. ===
            if (button == null) continue;

            if (!uiElements.ContainsKey(button.name))
            {
                uiElements.Add(button.name, button);
                print($"Button added: {button.name}");
            }
            else
            {
                Debug.LogWarning($"Duplicate button name found: {button.name}. Only the first occurrence will be stored.");
            }
        }
    }

    private void ShowResult()
    {
        Time.timeScale = 0;
        uiEndResult.SetActive(true);

        // === ADDED: refresh from the authoritative source when the panel opens. ===
        GameLoop loop = FindAnyObjectByType<GameLoop>();
        if (loop != null)
        {
            candyReceived.text = loop.Candy.ToString();
            monsterKilled.text = loop.MonsterKills.ToString();
        }
    }

    private void OnDestroy()
    {
        // === MODIFIED: null-guard EventBus (it may be destroyed before us on scene unload). ===
        if (EventBus.Instance != null)
        {
            EventBus.Instance.RemoveEventListener("EndResult", ShowResult);
            EventBus.Instance.RemoveEventListener<int>("CandyReceived", ShowCandyNum);
            EventBus.Instance.RemoveEventListener<int>("MonsterKilled", ShowMonsterNum);
        }

        // === ADDED: always restore time scale on teardown so the next scene isn't frozen. ===
        Time.timeScale = 1f;
    }
}