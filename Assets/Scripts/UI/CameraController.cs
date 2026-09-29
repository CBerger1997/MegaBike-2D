using UnityEngine;

public class CameraController : MonoBehaviour
{
    public float dragSpeed = 0.5f;
    private Vector3 dragOrigin;

    // Update is called once per frame
    void Update ()
    {
        if ( GameController.instance.gameState == GameController.GameState.Running )
        {
            if ( Input.mouseScrollDelta.y < 0 )
            {
                Camera.main.orthographicSize += 1;
            }
            else if ( Input.mouseScrollDelta.y > 0 )
            {
                Camera.main.orthographicSize -= 1;
            }

            Mathf.Clamp ( Camera.main.orthographicSize, 25, 100 );

            if ( Input.GetMouseButtonDown ( 0 ) )
            {
                dragOrigin = Input.mousePosition;
                return;
            }

            if ( !Input.GetMouseButton ( 0 ) )
                return;

            Vector3 pos = Camera.main.ScreenToViewportPoint ( Input.mousePosition - dragOrigin );
            Vector3 move = new Vector3 ( pos.x * dragSpeed, pos.y * dragSpeed, 0 );

            transform.Translate ( move, Space.World );
        }
    }
}
