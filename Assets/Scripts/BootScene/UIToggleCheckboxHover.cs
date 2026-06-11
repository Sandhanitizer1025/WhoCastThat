using UnityEngine;
using UnityEngine.EventSystems;

public class UIToggleCheckboxHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Checkbox Visual")]
    [SerializeField] private Transform checkboxVisual;

    [Header("Hover Settings")]
    [SerializeField] private float hoverScale = 1.15f;
    [SerializeField] private float speed = 10f;

    private Vector3 originalScale;
    private Vector3 targetScale;

    private void Awake()
    {
        if (checkboxVisual != null)
        {
            originalScale = checkboxVisual.localScale;
            targetScale = originalScale;
        }
    }

    private void Update()
    {
        if (checkboxVisual != null)
        {
            checkboxVisual.localScale = Vector3.Lerp(
                checkboxVisual.localScale,
                targetScale,
                Time.deltaTime * speed
            );
        }
    }


    public void OnPointerEnter(PointerEventData eventData)
    {
        if (checkboxVisual != null)
        {
            targetScale = originalScale * hoverScale;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (checkboxVisual != null)
        {
            targetScale = originalScale;
        }
    }
}