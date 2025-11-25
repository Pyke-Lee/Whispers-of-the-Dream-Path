using UnityEngine;

public class PlayerController : MonoBehaviour {
    [SerializeField] public float moveSpeed = 5.0f;
    [SerializeField] public float runSpeed = 8.0f;
    [SerializeField] public float jumpForce = 5.0f;
    [SerializeField] public float wallJumpPushPower = 5.0f;
    [SerializeField] public float airDrag = 2.0f;

    private Rigidbody rb;
    private Animator anim;
    private SpriteRenderer spriter;

    private Vector3 wallNormal;
    private bool isGroundedTemp;
    private bool isWallTemp;
    private float wallJumpCooldown = 0f;

    private float jumpIgnoreTimer = 0f;

    void Start() {
        anim = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();
        spriter = GetComponent<SpriteRenderer>();
    }

    void FixedUpdate() {
        anim.SetBool("IsGrounded", isGroundedTemp);
        anim.SetBool("IsWall", isWallTemp);

        if (isGroundedTemp) {
            anim.SetBool("Jump", false);

            Vector3 vel = rb.linearVelocity;
            vel.x = 0;
            rb.linearVelocity = vel;
        }
        else {
            ApplyAirResistance();
        }

        isGroundedTemp = false;
        isWallTemp = false;
    }

    void Update() {
        if (wallJumpCooldown > 0) {
            wallJumpCooldown -= Time.deltaTime;
        }
        if (jumpIgnoreTimer > 0) {
            jumpIgnoreTimer -= Time.deltaTime;
        }

        KeyInput();
    }

    void KeyInput() {
        if (wallJumpCooldown > 0) { return; }

        float dir = Input.GetAxisRaw("Horizontal");

        if (dir != 0) {
            spriter.flipX = dir < 0;
            anim.SetBool("Walk", true);
        }
        else {
            anim.SetBool("Walk", false);
        }

        if (Input.GetKey(KeyCode.LeftShift)) {
            anim.SetBool("Run", true);
        }
        else {
            anim.SetBool("Run", false);
        }

        float currentSpeed = anim.GetBool("Run") ? runSpeed : moveSpeed;

        transform.position += Vector3.right * dir * currentSpeed * Time.deltaTime * -1f;

        if (Input.GetKeyDown(KeyCode.Space)) {
            if (anim.GetBool("IsGrounded")) {
                Jump();
            }
            else if (anim.GetBool("IsWall")) {
                WallJump();
            }
        }
    }

    void ApplyAirResistance() {
        Vector3 vel = rb.linearVelocity;
        vel.x = Mathf.Lerp(vel.x, 0, airDrag * Time.fixedDeltaTime);
        rb.linearVelocity = vel;
    }

    void Jump() {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);

        anim.SetBool("Jump", true);
        anim.Play("Player_Jump", 0, 0f);
        anim.SetBool("IsGrounded", false);
        isGroundedTemp = false;

        jumpIgnoreTimer = 0.1f;
    }

    void WallJump() {
        anim.SetBool("IsWall", false);
        isWallTemp = false;

        anim.SetBool("Jump", true);
        anim.Play("Player_Jump", 0, 0f);

        Vector3 verticalForce = Vector3.up * jumpForce;
        Vector3 pushForce = wallNormal * wallJumpPushPower;

        rb.linearVelocity = Vector3.zero;
        rb.AddForce(verticalForce + pushForce, ForceMode.Impulse);

        if (wallNormal.x != 0) {
            spriter.flipX = wallNormal.x < 0;
        }

        wallJumpCooldown = 0.15f;

        jumpIgnoreTimer = 0.1f;
    }

    private void OnCollisionEnter(Collision collision) {
        CheckContactPoint(collision);
    }

    private void OnCollisionStay(Collision collision) {
        CheckContactPoint(collision);
    }

    private void CheckContactPoint(Collision collision) {
        if (jumpIgnoreTimer > 0) { return; }

        foreach (ContactPoint contact in collision.contacts) {
            Vector3 normal = contact.normal;

            if (normal.y > 0.7f) {
                isGroundedTemp = true;
                isWallTemp = false;
                return;
            }

            if (Mathf.Abs(normal.x) > 0.7f) {
                if (!isGroundedTemp) {
                    isWallTemp = true;
                    wallNormal = normal;
                }
            }
        }
    }
}