using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIMainMenu : MonoBehaviour
{
    private Dictionary<string, UIBehaviour> uiElements = new Dictionary<string, UIBehaviour>();
    [SerializeField] private GameObject panelMenu;
    [SerializeField] private GameObject panelNews;

    void Start()
    {
        FindUIElements<Button>(uiElements);

        (uiElements["BtnStart"] as Button).onClick.AddListener(OnStartButtonClicked);
        (uiElements["BtnSettings"] as Button).onClick.AddListener(OnSettingsButtonClicked);
        (uiElements["BtnQuit"] as Button).onClick.AddListener(OnQuitButtonClicked);
        (uiElements["BtnContinue"] as Button).onClick.AddListener(OnContinueButtonClicked);

        panelNews.SetActive(false);

    }


    private void FindUIElements<T>(Dictionary<string, UIBehaviour> uiElements) where T:UIBehaviour
    {
        T[] buttons = GetComponentsInChildren<T>(true);
        foreach (T button in buttons)
        {
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

    private void OnContinueButtonClicked()
    {
        panelNews.SetActive(false);
        // To Do: load next scene
    }

    private void OnQuitButtonClicked()
    {
#if UNITY_EDITOR
        EditorApplication.ExitPlaymode();
#endif
        Application.Quit();
    }

    private void OnSettingsButtonClicked()
    {
        throw new NotImplementedException();
    }

    private void OnStartButtonClicked()
    {
        panelMenu.SetActive(false);
        panelNews.SetActive(true);
    }

}
