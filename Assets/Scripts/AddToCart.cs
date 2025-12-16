using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AddToCart : MonoBehaviour
{
    private TrialController trialController;
    private HashSet<GameObject> processedProducts = new HashSet<GameObject>();

    [Header("Physics Settlement")]
    [SerializeField] private float velocityThreshold = 0.1f; // How slow before considered settled
    [SerializeField] private float settlementTime = 0.5f; // How long velocity must stay low

    void Start()
    {
        trialController = FindFirstObjectByType<TrialController>();
        if (trialController == null)
        {
            Debug.LogError("AddToCart: TrialController not found!");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        ProductData productData = other.GetComponent<ProductData>();

        if (productData == null)
        {
            productData = other.GetComponentInParent<ProductData>();
        }

        if (productData != null)
        {
            GameObject productObject = productData.gameObject;

            // Check if already processed
            if (processedProducts.Contains(productObject))
                return;

            // Mark as processed immediately (prevent duplicate coroutines)
            processedProducts.Add(productObject);

            Debug.Log($"Product {productData.productID} entered cart - waiting for settlement...");

            // Start coroutine to wait for settlement
            StartCoroutine(WaitForSettlementAndProcess(productObject, productData));
        }
    }

    private IEnumerator WaitForSettlementAndProcess(GameObject productObject, ProductData productData)
    {
        Rigidbody rb = productObject.GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogWarning($"Product {productData.productID} has no Rigidbody!");
            yield break;
        }

        // OPTION 1: Wait for Rigidbody.IsSleeping (Unity's built-in detection)
        while (!rb.IsSleeping())
        {
            yield return new WaitForFixedUpdate();
        }

        /* OPTION 2: Manual velocity check (more control)
        float timeSettled = 0f;
        
        while (timeSettled < settlementTime)
        {
            // Check if velocity is low enough
            if (rb.velocity.magnitude < velocityThreshold && rb.angularVelocity.magnitude < velocityThreshold)
            {
                timeSettled += Time.fixedDeltaTime;
            }
            else
            {
                timeSettled = 0f; // Reset timer if object moves again
            }
            
            yield return new WaitForFixedUpdate();
        }
        */

        Debug.Log($"Product {productData.productID} settled - adding to cart");

        // Now it's safe to disable physics
        TrialProduct trialProduct = productObject.GetComponentInParent<TrialProduct>();

        if (trialController != null && trialProduct != null)
        {
            trialController.MarkProductAsPurchased(trialProduct);
        }

        ConvertToStaticProduct(productObject);
    }

    void ConvertToStaticProduct(GameObject product)
    {
        // Disable Meta Interaction SDK components
        HandGrabInteractable handGrab = product.GetComponentInChildren<HandGrabInteractable>();
        GrabInteractable grabInteractable = product.GetComponentInChildren<GrabInteractable>();
        Grabbable grabbable = product.GetComponent<Grabbable>();

        if (handGrab != null) handGrab.enabled = false;
        if (grabInteractable != null) grabInteractable.enabled = false;
        if (grabbable != null) grabbable.enabled = false;

        // NOW make rigidbody kinematic (after it's settled)
        Rigidbody rb = product.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        Debug.Log($"✓ Product {product.name} purchased and locked in cart");
    }
}
