using UnityEngine;

public class PlayerControl : MonoBehaviour {
    Animator anim;
    SpriteRenderer spriter;
    Rigidbody rb;

    [SerializeField] float moveSpeed = 1.0f;

    void Start() {
        anim = GetComponent<Animator>();
        spriter = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody>();
    }

    void Update() {

    }
}
