using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 5f;

    [Header("References")]
    public VariableJoystick variableJoystick;
    public Rigidbody rb;
    public Animator animator;

    private void FixedUpdate()
    {
       
        Vector3 direction = new Vector3(variableJoystick.Horizontal, 0, variableJoystick.Vertical);

        
        if (direction.magnitude > 0.1f)
        {
            Vector3 move = direction.normalized * speed * Time.fixedDeltaTime;
            rb.MovePosition(rb.position + move);

            transform.forward = direction.normalized;
        }

        animator.SetFloat("Speed", direction.magnitude);
    }
}
