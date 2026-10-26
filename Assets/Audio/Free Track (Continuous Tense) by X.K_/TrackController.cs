using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace XKEnterteiment
{
    public class TrackController : MonoBehaviour
    {
        public AudioSource audioSource;

        public Slider audioSlider;

        void Start()
        {
            audioSlider.onValueChanged.AddListener(OnSliderValueChanged);
        }

        void Update()
        {
            if (audioSource.isPlaying)
            {
                audioSlider.value = (float)audioSource.timeSamples / audioSource.clip.samples;
                // Calculate the current time position as a fraction of the total samples
                float sliderValue = (float)audioSource.timeSamples / audioSource.clip.samples;

                // Update the audio slider's value
                audioSlider.value = sliderValue;
            }
        }

        public void OnSliderValueChanged(float value)
        {
            audioSource.timeSamples = (int)(value * audioSource.clip.samples);
        }
    }
}

