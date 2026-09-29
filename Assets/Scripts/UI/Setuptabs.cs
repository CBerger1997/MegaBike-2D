using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tabs the setup screen by bike. One page per bike, and pages beyond the
/// number of bikes in play hide themselves - so a single-bike setup shows no
/// tab bar at all.
///
/// Purely visual: SetupUI still holds one flat list of rows, so nothing about
/// how the roster is built changes.
/// </summary>
public class SetupTabs : MonoBehaviour
{
    [System.Serializable]
    public class TabPage
    {
        [Tooltip ( "The button that selects this page." )]
        public Button button;

        [Tooltip ( "The panel holding this bike's rider rows." )]
        public GameObject panel;

        [Tooltip ( "Optional label, e.g. \"Bike 1\"." )]
        public TMP_Text label;
    }

    [Header ( "Pages" )]
    [Tooltip ( "One per bike, in bike order." )]
    public List<TabPage> pages = new List<TabPage> ();

    [Header ( "Appearance" )]
    [Tooltip ( "The tab bar itself. Hidden when only one bike is in play." )]
    public GameObject tabBar;

    public Color activeTabColour = new Color ( 0.95f, 0.95f, 0.95f );
    public Color inactiveTabColour = new Color ( 0.65f, 0.65f, 0.65f );

    [Header ( "Labels" )]
    [Tooltip ( "{0} is replaced with the bike number." )]
    public string labelFormat = "Bike {0}";

    private int activeIndex;

    private void Awake ()
    {
        for ( int i = 0; i < pages.Count; i++ )
        {
            int index = i;

            if ( pages[ i ].button != null )
            {
                pages[ i ].button.onClick.RemoveAllListeners ();
                pages[ i ].button.onClick.AddListener ( () => Select ( index ) );
            }

            if ( pages[ i ].label != null )
                pages[ i ].label.text = string.Format ( labelFormat, i + 1 );
        }
    }

    private void Start ()
    {
        Refresh ();
    }

    /// <summary>Shows only the tabs for bikes that exist, then selects a valid one.</summary>
    public void Refresh ()
    {
        int bikeCount = BikeCount ();

        for ( int i = 0; i < pages.Count; i++ )
        {
            bool inUse = i < bikeCount;

            if ( pages[ i ].button != null )
                pages[ i ].button.gameObject.SetActive ( inUse );

            if ( !inUse && pages[ i ].panel != null )
                pages[ i ].panel.SetActive ( false );
        }

        // A single bike needs no tab bar.
        if ( tabBar != null )
            tabBar.SetActive ( bikeCount > 1 );

        Select ( Mathf.Clamp ( activeIndex, 0, Mathf.Max ( 0, bikeCount - 1 ) ) );
    }

    public void Select ( int index )
    {
        activeIndex = index;

        int bikeCount = BikeCount ();

        for ( int i = 0; i < pages.Count; i++ )
        {
            bool visible = i == index && i < bikeCount;

            if ( pages[ i ].panel != null )
                pages[ i ].panel.SetActive ( visible );

            if ( pages[ i ].button != null )
            {
                ColorBlock colours = pages[ i ].button.colors;
                colours.normalColor = visible ? activeTabColour : inactiveTabColour;
                colours.selectedColor = colours.normalColor;
                pages[ i ].button.colors = colours;
            }
        }
    }

    private int BikeCount ()
    {
        if ( GameController.instance != null && GameController.instance.bikes.Count > 0 )
            return Mathf.Min ( GameController.instance.bikes.Count, pages.Count );

        return Mathf.Min ( 1, pages.Count );
    }
}