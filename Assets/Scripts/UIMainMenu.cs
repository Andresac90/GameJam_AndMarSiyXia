using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIMainMenu : MonoBehaviour
{
    private Dictionary<string, UIBehaviour> uiButtons = new Dictionary<string, UIBehaviour>();

    void Start()
    {
        Button[] buttons = GetComponentsInChildren<Button>();
        foreach (Button button in buttons)
        {
            if (!uiButtons.ContainsKey(button.name))
            {
                uiButtons.Add(button.name, button);
                print($"Button added: {button.name}");
            }
            else
            {
                Debug.LogWarning($"Duplicate button name found: {button.name}. Only the first occurrence will be stored.");
            }
        }

        (uiButtons["BtnStart"] as Button).onClick.AddListener(OnStartButtonClicked);
        (uiButtons["BtnSettings"] as Button).onClick.AddListener(OnSettingsButtonClicked);
        (uiButtons["BtnExit"] as Button).onClick.AddListener(OnQuitButtonClicked);

    }

    private void OnQuitButtonClicked()
    {
        // Handle quit button click
        Application.Quit();
    }

    private void OnSettingsButtonClicked()
    {
        throw new NotImplementedException();
    }

    private void OnStartButtonClicked()
    {
        throw new NotImplementedException();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
