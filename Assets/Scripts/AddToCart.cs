using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AddToCart : MonoBehaviour
{
    private TrialController trialController;
    private HashSet<GameObject> processedProducts = new HashSet<GameObject>();

    [Header("Trial Management")]
    [SerializeField] private Transform cartSurface; // The surface where products will be placed
    [SerializeField] private Transform cartItemsParent; // ✓ ASSIGN THIS IN THE INSPECTOR!

    [Header("Physics Settlement")]
    [SerializeField] private float velocityThreshold = 0.1f; // How slow before considered settled
    [SerializeField] private float settlementTime = 0.5f; // How long velocity must stay low

    [Header("Organized Placement")]
    [SerializeField] private bool useOrganizedPlacement = true; // Toggle for organized vs. natural drop
    [SerializeField] private float gridMargin = 0.02f; // 2cm margin from edges
    [SerializeField] private float gridSpacing = 0.02f; // 2cm space between products

    private List<Bounds> placedItemsBounds = new List<Bounds>();

    void Start()
    {
        trialController = FindFirstObjectByType<TrialController>();
        if (trialController == null)
        {
            Debug.LogError("AddToCart: TrialController not found!");
        }

        // Validate that cartItemsParent is assigned
        if (useOrganizedPlacement && cartItemsParent == null)
        {
            Debug.LogError("AddToCart: cartItemsParent is not assigned! Please assign CartItems_Organized in the Inspector.");
        }
        else if (cartItemsParent != null)
        {
            Debug.Log($"✓ CartItems_Organized found at position: {cartItemsParent.position}");
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
            // ✓ The ProductData GameObject is what we want to copy (the actual product mesh/collider)
            GameObject productObject = productData.gameObject;

            // Get the parent TrialProduct for marking as purchased
            TrialProduct trialProduct = productData.GetComponentInParent<TrialProduct>();

            if (trialProduct == null)
            {
                Debug.LogError($"ProductData found but no parent TrialProduct for {productData.gameObject.name}");
                return;
            }

            // Check if already processed (use the TrialProduct as the key to prevent duplicates)
            if (processedProducts.Contains(trialProduct.gameObject))
                return;

            // Mark as processed immediately (prevent duplicate coroutines)
            processedProducts.Add(trialProduct.gameObject);

            Debug.Log($"Product {productData.productID} entered cart - waiting for settlement...");

            // Start coroutine to wait for settlement
            StartCoroutine(WaitForSettlementAndProcess(productObject, productData, trialProduct));
        }
    }

    private IEnumerator WaitForSettlementAndProcess(GameObject productObject, ProductData productData, TrialProduct trialProduct)
    {
        Rigidbody rb = productObject.GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogWarning($"Product {productData.productID} has no Rigidbody! Checking parent...");
            rb = productObject.GetComponentInParent<Rigidbody>();

            if (rb == null)
            {
                Debug.LogError($"No Rigidbody found for product {productData.productID}!");
                yield break;
            }
        }

        // Wait for Rigidbody.IsSleeping (Unity's built-in detection)
        while (!rb.IsSleeping())
        {
            yield return new WaitForFixedUpdate();
        }

        Debug.Log($"Product {productData.productID} settled - adding to cart");

        // Mark as purchased in trial controller
        if (trialController != null && trialProduct != null)
        {
            trialController.MarkProductAsPurchased(trialProduct);
        }

        // Organize or keep natural placement
        if (useOrganizedPlacement && cartItemsParent != null)
        {
            OrganizeProductInCart(productObject);
        }
        else
        {
            ConvertToStaticProduct(productObject);
        }

        Debug.Log($"About to call NextTrial for product {productData.productID}");

        // Proceed to next trial after product is secured
        if (trialController != null)
        {
            trialController.NextTrial();
        }
        else
        {
            Debug.LogError("TrialController is null! Cannot proceed to next trial.");
        }
    }

    void OrganizeProductInCart(GameObject product)
    {
        Debug.Log($"OrganizeProductInCart called for {product.name}");

        // Create an independent copy of the product for the cart
        GameObject cartCopy = Instantiate(product);
        cartCopy.name = product.name + "_CartCopy";

        Debug.Log($"Created cart copy: {cartCopy.name}");

        // Parent to CartItems_Organized - use worldPositionStays=false for clean hierarchy
        cartCopy.transform.SetParent(cartItemsParent, false);

        // Remove all TrialProduct and ProductData references from the copy
        foreach (var tp in cartCopy.GetComponentsInChildren<TrialProduct>(true))
            Destroy(tp);
        foreach (var pd in cartCopy.GetComponentsInChildren<ProductData>(true))
            Destroy(pd);

        // ✓ COMPLETELY REMOVE all Meta SDK interaction components
        RemoveAllInteractionComponents(cartCopy);

        // Make all rigidbodies kinematic and disable gravity
        foreach (var rb in cartCopy.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // ✓ RESET TRANSFORMS to clean state BEFORE getting bounds
        cartCopy.transform.localRotation = Quaternion.identity;
        cartCopy.transform.localScale = Vector3.one;
        cartCopy.transform.localPosition = Vector3.zero; // Temporary position for bounds calculation

        // ✓ Get bounds AFTER removing components and resetting transforms
        Bounds itemBounds = GetCombinedBounds(cartCopy);
        Vector3 itemSize = itemBounds.size;

        Debug.Log($"Item size: {itemSize}");

        // ✓ Calculate grid position relative to cartSurface dimensions
        Vector3 bestPosition = FindAvailablePosition(itemSize);

        Debug.Log($"Best position calculated: {bestPosition}");

        // ✓ Position the copy at the calculated grid position
        // The position is relative to cartItemsParent (which should be at cartSurface)
        cartCopy.transform.localPosition = bestPosition;

        // Store bounds for future collision detection
        placedItemsBounds.Add(new Bounds(bestPosition, itemSize));

        Debug.Log($"✓ Product {cartCopy.name} copied with clean transforms and all interactivity removed");
        Debug.Log($"✓ Final local position: {cartCopy.transform.localPosition}");
        Debug.Log($"✓ CartItems_Organized child count: {cartItemsParent.childCount}");

        // Hide the original product immediately so it can be reused by TrialController
        product.SetActive(false);
    }

    void RemoveAllInteractionComponents(GameObject obj)
    {
        // Remove ALL Meta Interaction SDK components from the copy and its children

        // Hand interaction components
        foreach (var component in obj.GetComponentsInChildren<HandGrabInteractable>(true))
            Destroy(component);
        foreach (var component in obj.GetComponentsInChildren<HandGrabInteractor>(true))
            Destroy(component);
        foreach (var component in obj.GetComponentsInChildren<HandGrabPose>(true))
            Destroy(component);

        // Grab interaction components
        foreach (var component in obj.GetComponentsInChildren<GrabInteractable>(true))
            Destroy(component);
        foreach (var component in obj.GetComponentsInChildren<GrabInteractor>(true))
            Destroy(component);
        foreach (var component in obj.GetComponentsInChildren<Grabbable>(true))
            Destroy(component);

        // Distance interaction components
        foreach (var component in obj.GetComponentsInChildren<DistanceGrabInteractable>(true))
            Destroy(component);
        foreach (var component in obj.GetComponentsInChildren<DistanceGrabInteractor>(true))
            Destroy(component);

        // Snap interaction components
        foreach (var component in obj.GetComponentsInChildren<SnapInteractable>(true))
            Destroy(component);
        foreach (var component in obj.GetComponentsInChildren<SnapInteractor>(true))
            Destroy(component);

        // Touchable components
        foreach (var component in obj.GetComponentsInChildren<TouchHandGrabInteractable>(true))
            Destroy(component);

        // Ray interaction components
        foreach (var component in obj.GetComponentsInChildren<RayInteractable>(true))
            Destroy(component);
        foreach (var component in obj.GetComponentsInChildren<RayInteractor>(true))
            Destroy(component);

        // Poke interaction components
        foreach (var component in obj.GetComponentsInChildren<PokeInteractable>(true))
            Destroy(component);
        foreach (var component in obj.GetComponentsInChildren<PokeInteractor>(true))
            Destroy(component);

        // Remove any remaining components from the Oculus.Interaction namespace
        Component[] allComponents = obj.GetComponentsInChildren<Component>(true);
        foreach (var component in allComponents)
        {
            if (component != null && component.GetType().Namespace != null &&
                component.GetType().Namespace.StartsWith("Oculus.Interaction"))
            {
                // Don't destroy Transform or GameObject
                if (!(component is Transform) && !(component is GameObject))
                {
                    Destroy(component);
                }
            }
        }

        Debug.Log($"✓ All Meta SDK interaction components removed from {obj.name}");
    }

    void ConvertToStaticProduct(GameObject product)
    {
        // Disable Meta Interaction SDK components (fallback for non-organized placement)
        HandGrabInteractable handGrab = product.GetComponentInChildren<HandGrabInteractable>();
        GrabInteractable grabInteractable = product.GetComponentInChildren<GrabInteractable>();
        Grabbable grabbable = product.GetComponent<Grabbable>();

        if (handGrab != null) handGrab.enabled = false;
        if (grabInteractable != null) grabInteractable.enabled = false;
        if (grabbable != null) grabbable.enabled = false;

        // Make rigidbody kinematic
        Rigidbody rb = product.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        Debug.Log($"✓ Product {product.name} converted to static (non-organized mode)");
    }

    private Vector3 FindAvailablePosition(Vector3 itemSize)
    {
        // ✓ Use cartSurface dimensions to define the placement area
        // Get the actual size of the cart surface (not scale)
        Renderer surfaceRenderer = cartSurface.GetComponent<Renderer>();
        Vector3 surfaceSize;

        if (surfaceRenderer != null)
        {
            surfaceSize = surfaceRenderer.bounds.size;
        }
        else
        {
            // Fallback to using localScale if no renderer
            surfaceSize = new Vector3(
                cartSurface.localScale.x,
                cartSurface.localScale.y,
                cartSurface.localScale.z
            );
        }

        float surfaceWidth = surfaceSize.x;
        float surfaceDepth = surfaceSize.z;

        Debug.Log($"CartSurface dimensions - Width: {surfaceWidth}, Depth: {surfaceDepth}");

        // ✓ Calculate boundaries with 4cm margin from edges
        float minX = -surfaceWidth / 2 + gridMargin;
        float maxX = surfaceWidth / 2 - gridMargin;
        float minZ = -surfaceDepth / 2 + gridMargin;
        float maxZ = surfaceDepth / 2 - gridMargin;

        Debug.Log($"Grid boundaries - MinX: {minX}, MaxX: {maxX}, MinZ: {minZ}, MaxZ: {maxZ}");
        Debug.Log($"Item size - Width: {itemSize.x}, Depth: {itemSize.z}, Height: {itemSize.y}");
        Debug.Log($"Currently placed items: {placedItemsBounds.Count}");

        // Start from the corner
        float currentZ = minZ;

        // Try to find a non-colliding position in a grid layout (row by row)
        while (currentZ + itemSize.z <= maxZ)
        {
            float currentX = minX; // Reset X for each new row

            while (currentX + itemSize.x <= maxX)
            {
                // ✓ Position is the CENTER of the item
                Vector3 candidatePos = new Vector3(
                    currentX + itemSize.x / 2,
                    itemSize.y / 2, // Place item so its bottom sits on the surface
                    currentZ + itemSize.z / 2
                );

                Bounds candidateBounds = new Bounds(candidatePos, itemSize);

                Debug.Log($"Testing position: {candidatePos}");

                // Check if this position collides with any placed items
                if (!CollidesWithPlacedItems(candidateBounds))
                {
                    Debug.Log($"✓ Found available position: {candidatePos}");
                    return candidatePos;
                }
                else
                {
                    Debug.Log($"✗ Position {candidatePos} collides with existing items");
                }

                // ✓ Move to next X position (item width + 3cm spacing)
                currentX += itemSize.x + gridSpacing;
            }

            // ✓ Move to next row (item depth + 3cm spacing)
            currentZ += itemSize.z + gridSpacing;
        }

        // Fallback: stack on top if no horizontal space found
        float stackHeight = 0;
        foreach (var bounds in placedItemsBounds)
        {
            if (bounds.max.y > stackHeight)
                stackHeight = bounds.max.y;
        }
        
        Vector3 stackedPos = new Vector3(0, stackHeight + itemSize.y / 2, 0);
        Debug.LogWarning($"⚠ No grid space available, stacking at: {stackedPos}");
        return stackedPos;
    }

    private bool CollidesWithPlacedItems(Bounds candidateBounds)
    {
        for (int i = 0; i < placedItemsBounds.Count; i++)
        {
            Bounds placedBounds = placedItemsBounds[i];
            
            if (candidateBounds.Intersects(placedBounds))
            {
                Debug.Log($"  Collision detected with item {i}: candidate center={candidateBounds.center}, size={candidateBounds.size}, placed center={placedBounds.center}, size={placedBounds.size}");
                return true;
            }
        }
        return false;
    }

    private Bounds GetCombinedBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            Collider col = obj.GetComponent<Collider>();
            if (col != null)
                return col.bounds;

            return new Bounds(obj.transform.position, Vector3.one * 0.1f);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    public void ClearCartItems()
    {
        if (cartItemsParent != null)
        {
            foreach (Transform child in cartItemsParent)
            {
                Destroy(child.gameObject);
            }
        }

        placedItemsBounds.Clear();
        processedProducts.Clear();

        Debug.Log("Cart cleared for new trial block");
    }
}