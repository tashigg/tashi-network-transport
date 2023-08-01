using UnityEngine;

public class Player : MonoBehaviour
{
    public Rigidbody2D rigidBody;
    public SpriteRenderer spriteRenderer;

    void Update()
    {
        var x = Input.GetAxisRaw("Horizontal");
        var y = Input.GetAxisRaw("Vertical");
        rigidBody.velocity = new Vector2(x, y) * 30;
    }

    private void Start()
    {
    }
}
