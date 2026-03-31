using UnityEngine;

public class SurvivorMove : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private Transform cameraYawRoot;
    [SerializeField] private Transform cameraPitchRoot;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Animator animator;

    [Header("속도")]
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float runSpeed = 4f;
    [SerializeField] private float crouchSpeed = 1f;
    [SerializeField] private float turnSpeed = 15f;

    [Header("카메라")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float minPitch = -60f;
    [SerializeField] private float maxPitch = 60f;

    private CharacterController controller;
    private SurvivorInput input;

    private float cameraYaw;
    private float pitch;
    private float yVelocity;
    private bool isMoveLocked;

    public void SetMoveLock(bool value)
    {
        isMoveLocked = value;
    }

    public void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;

        if (dir.sqrMagnitude <= 0.001f)
            return;

        if (modelRoot != null)
            modelRoot.rotation = Quaternion.LookRotation(dir.normalized);
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<SurvivorInput>();

        if (animator == null && modelRoot != null)
            animator = modelRoot.GetComponentInChildren<Animator>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (controller == null || !controller.enabled)
            return;

        Look();

        if (isMoveLocked)
        {
            ApplyGravityOnly();
            UpdateAnimator(0f, input.IsCrouching);
            return;
        }

        Move();
        Crouch();
    }

    private void Look()
    {
        Vector2 look = input.Look;

        cameraYaw += look.x * mouseSensitivity;
        pitch -= look.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        if (cameraYawRoot != null)
            cameraYawRoot.localRotation = Quaternion.Euler(0f, cameraYaw, 0f);

        if (cameraPitchRoot != null)
            cameraPitchRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void Move()
    {
        Vector2 moveInput = input.Move;

        Vector3 forward = playerCamera.transform.forward;
        Vector3 right = playerCamera.transform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        Vector3 move = forward * moveInput.y + right * moveInput.x;

        if (move.magnitude > 1f)
            move.Normalize();

        bool isMoving = move.sqrMagnitude > 0.001f;
        bool isRunning = isMoving && !input.IsCrouching && input.IsRunning;

        float speed = walkSpeed;
        float animSpeed = 0f;

        if (input.IsCrouching)
        {
            speed = crouchSpeed;
            animSpeed = isMoving ? 0.5f : 0f;
        }
        else if (isRunning)
        {
            speed = runSpeed;
            animSpeed = 1f;
        }
        else if (isMoving)
        {
            speed = walkSpeed;
            animSpeed = 0.5f;
        }

        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.deltaTime;

        Vector3 finalMove = move;
        finalMove.y = yVelocity / speed;

        controller.Move(finalMove * speed * Time.deltaTime);

        if (isMoving && modelRoot != null)
        {
            Quaternion targetRot = Quaternion.LookRotation(move);

            modelRoot.rotation = Quaternion.Slerp(
                modelRoot.rotation,
                targetRot,
                turnSpeed * Time.deltaTime
            );
        }

        UpdateAnimator(animSpeed, input.IsCrouching);
    }

    private void ApplyGravityOnly()
    {
        if (controller == null || !controller.enabled)
            return;

        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.deltaTime;

        Vector3 gravityMove = new Vector3(0f, yVelocity, 0f);
        controller.Move(gravityMove * Time.deltaTime);
    }

    private void Crouch()
    {
        if (input.IsCrouching)
        {
            controller.height = 1.2f;
            // 높이가 1.2면 중심은 0.6여야 발바닥이 0 위치에 고정
            controller.center = new Vector3(0f, 0.6f, 0f);
        }
        else
        {
            controller.height = 1.8f;
            // 높이가 1.8이면 중심은 0.9
            controller.center = new Vector3(0f, 0.9f, 0f);
        }
    }

    private void UpdateAnimator(float targetMoveSpeed, bool isCrouching)
    {
        animator.SetFloat("MoveSpeed", targetMoveSpeed, 0.1f, Time.deltaTime);
        animator.SetBool("IsCrouching", isCrouching);
    }

    public void PlayAnimation(string triggerName)
    {
        animator.SetTrigger(triggerName);
    }

    public void StopAnimation()
    {
        UpdateAnimator(0f, input != null && input.IsCrouching);
    }
}