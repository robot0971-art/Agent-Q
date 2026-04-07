using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class PorkDamageFlash : MonoBehaviour
{
    [Header("Flash Settings")]
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float flashDuration = 0.1f;
    [SerializeField] private float flashIntensity = 2.0f;
    
    [Header("Collision Settings")]
    [SerializeField] private string damageTag = "Weapon";
    [SerializeField] private bool useTrigger = true;
    
    private Renderer _renderer;
    private MaterialPropertyBlock _propertyBlock;
    private Color _originalEmissionColor;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    
    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _propertyBlock = new MaterialPropertyBlock();
        
        // Store original emission color
        _renderer.GetPropertyBlock(_propertyBlock);
        _originalEmissionColor = _propertyBlock.GetColor(EmissionColor);
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (!useTrigger) return;
        
        if (other.CompareTag(damageTag) || damageTag == "")
        {
            TriggerFlash();
        }
    }
    
    private void OnCollisionEnter(Collision collision)
    {
        if (useTrigger) return;
        
        if (collision.gameObject.CompareTag(damageTag) || damageTag == "")
        {
            TriggerFlash();
        }
    }
    
    public void TriggerFlash()
    {
        StopAllCoroutines();
        StartCoroutine(FlashCoroutine());
    }
    
    private System.Collections.IEnumerator FlashCoroutine()
    {
        // Apply flash color
        _renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(EmissionColor, flashColor * flashIntensity);
        _renderer.SetPropertyBlock(_propertyBlock);
        
        // Wait for duration
        yield return new WaitForSeconds(flashDuration);
        
        // Restore original color
        _renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(EmissionColor, _originalEmissionColor);
        _renderer.SetPropertyBlock(_propertyBlock);
    }
    
    // Public method to trigger flash from other scripts
    public void TakeDamage(float damage)
    {
        TriggerFlash();
        // Add damage logic here if needed
    }
}
