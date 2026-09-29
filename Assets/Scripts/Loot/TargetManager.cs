using System.Collections.Generic;
using UnityEngine;

public class TargetManager : MonoBehaviour
{
    public List<CollectibleBox> boxes =
        new List<CollectibleBox> ();

    public Transform CurrentTarget { get; private set; }

    private void Start ()
    {
        RefreshBoxes ();

        if ( boxes.Count > 0 )
            SelectTarget ( boxes[ 0 ] );
    }

    private void Update ()
    {
        HandleNumberKeys ();
    }

    private void HandleNumberKeys ()
    {
        for ( int i = 0; i < boxes.Count && i < 9; i++ )
        {
            KeyCode numberKey =
                KeyCode.Alpha1 + i;

            if ( Input.GetKeyDown ( numberKey ) )
                SelectTarget ( boxes[ i ] );
        }
    }

    public void RefreshBoxes ()
    {
        boxes.Clear ();

        CollectibleBox[] foundBoxes =
            FindObjectsByType<CollectibleBox> (
                FindObjectsSortMode.None
            );

        boxes.AddRange ( foundBoxes );
        RemoveMissingBoxes ();
    }

    public void SelectTarget ( CollectibleBox box )
    {
        if ( box == null )
            return;

        RemoveMissingBoxes ();

        CurrentTarget = box.transform;
    }

    public void BoxCollected ( CollectibleBox collectedBox )
    {
        boxes.Remove ( collectedBox );

        if ( CurrentTarget == collectedBox.transform )
            CurrentTarget = null;

        RemoveMissingBoxes ();

        if ( boxes.Count > 0 )
            SelectTarget ( boxes[ 0 ] );
    }

    private void RemoveMissingBoxes ()
    {
        boxes.RemoveAll ( box => box == null );
    }
}