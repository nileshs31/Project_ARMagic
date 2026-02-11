using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpellFadeTester : MonoBehaviour
{
    AudioSource audioSource;
    Animator animator;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        animator = GetComponent<Animator>();
    }
    public void OnEnable()
    {

        if (audioSource != null && audioSource.clip != null)
        {
            audioSource.Play();
            StartCoroutine(WaitForAudioThenFade());
        }
        else
        {
            // No audio? Fade immediately
            TriggerFade();
        }
    }
    IEnumerator WaitForAudioThenFade()
    {
        // Wait for the actual clip length (pitch-safe)
        yield return new WaitForSeconds(audioSource.clip.length / audioSource.pitch);
        TriggerFade();
    }
    void TriggerFade()
    {
        if (animator != null)
        {
            animator.Play("fadeaway");
        }
        else
        {
            SpellOffer();
        }
    }
    // Update is called once per frame
    void Update()
    {
        
    }

    public void SpellOffer()
    {
        this.gameObject.SetActive(false);
    }
}
