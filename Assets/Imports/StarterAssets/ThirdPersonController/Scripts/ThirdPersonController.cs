using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM 
using UnityEngine.InputSystem;
using UnityEngine.UI;
#endif

/* Note: animations are called via the controller for both the character and capsule using animator null checks
 */

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM 
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] private float _aimShrinkSpeed = 0.5f; // how fast it shrinks per second
        [SerializeField] private float _minCrosshairScale = 0.5f; // smallest allowed crosshair size
        [Tooltip("Move speed of the character in m/s")]
        public float MoveSpeed = 2.0f;

        [Tooltip("Sprint speed of the character in m/s")]
        public float SprintSpeed = 5.335f;

        [Tooltip("Roll distance of the character in m")]
        public float RollDistance = 2f;

        [Tooltip("Roll Movement Curve")]
        public AnimationCurve rollCurve =            // 0..1 -> fraction of distance
        AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Tooltip("How fast the character turns to face movement direction")]
        [Range(0.0f, 0.3f)]
        public float RotationSmoothTime = 0.12f;

        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float JumpTimeout = 0.50f;

        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool Grounded = true;

        [Tooltip("Useful for rough ground")]
        public float GroundedOffset = -0.14f;

        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float GroundedRadius = 0.28f;

        [Tooltip("What layers the character uses as ground")]
        public LayerMask GroundLayers;

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
        public GameObject CinemachineCameraTarget;

        [Tooltip("How far in degrees can you move the camera up")]
        public float TopClamp = 70.0f;

        [Tooltip("How far in degrees can you move the camera down")]
        public float BottomClamp = -30.0f;

        [Tooltip("Additional degress to override the camera. Useful for fine tuning camera position when locked")]
        public float CameraAngleOverride = 0.0f;

        [Tooltip("For locking the camera position on all axis")]
        public bool LockCameraPosition = false;

        [Header("Gun")]
        public List<Texture2D> ammoTextures;
        public RawImage ammoDisplay;
        public RawImage crosshair;
        [SerializeField] private TrailRenderer tracerPrefab;
        [SerializeField] private Transform muzzlePoint;
        [SerializeField] private float tracerDuration = 0.05f;

        // cinemachine
        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;

        // player
        private float _speed;
        private float _rollDistanceFraction; // how much the roll progressed
        private float _rollTotalDistance;
        private float _rollDuration = 1.16f;
        private float _distRolled;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;
        private float _aimTimer = 0f;
        private float _crosshairScale = 1.25f;  // starting size
        private float _fireDelay = 0.5f; // time between shots
        private int maxAmmo = 6;
        private int currentAmmo;

        // timeout deltatime
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;
        private float _lastFireTime = 0f;

        // animation IDs
        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;
        private int _animIDRollForward;

#if ENABLE_INPUT_SYSTEM 
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;

        private const float _threshold = 0.01f;

        private bool _hasAnimator;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput.currentControlScheme == "KeyboardMouse";
#else
				return false;
#endif
            }
        }


        private void Awake()
        {
            // get a reference to our main camera
            if (_mainCamera == null)
            {
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            }
        }

        private void Start()
        {
            _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;

            _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM 
            _playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

            AssignAnimationIDs();

            currentAmmo = maxAmmo;
            ammoDisplay.texture = ammoTextures[currentAmmo];
            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }

        private void Update()
        {
            _hasAnimator = TryGetComponent(out _animator);

            JumpAndGravity();
            GroundedCheck();
            Move();
            Roll();
            Reload();
            Aim();
        }

        private void LateUpdate()
        {
            CameraRotation();
        }

        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
            _animIDRollForward = Animator.StringToHash("Roll");
        }

        private void GroundedCheck()
        {
            // set sphere position, with offset
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
                transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
                QueryTriggerInteraction.Ignore);

            // update animator if using character
            if (_hasAnimator)
            {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        private void CameraRotation()
        {
            // if there is an input and camera position is not fixed
            if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                //Don't multiply mouse input by Time.deltaTime;
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetYaw += _input.look.x * deltaTimeMultiplier;
                _cinemachineTargetPitch += _input.look.y * deltaTimeMultiplier;
            }

            // clamp our rotations so our values are limited 360 degrees
            _cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            // Cinemachine will follow this target
            CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch + CameraAngleOverride,
                _cinemachineTargetYaw, 0.0f);
        }

        private void Move()
        {
            // set target speed based on move speed, sprint speed and if sprint is pressed
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            // a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

            // note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is no input, set the target speed to 0
            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            // a reference to the players current horizontal velocity
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // accelerate or decelerate to target speed
            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
                currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                // creates curved result rather than a linear one giving a more organic speed change
                // note T in Lerp is clamped, so we don't need to clamp our speed
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                    Time.deltaTime * SpeedChangeRate);

                // round speed to 3 decimal places
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            // normalise input direction
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            // note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is a move input rotate player when the player is moving
            if (_input.move != Vector2.zero)
            {
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                                  _mainCamera.transform.eulerAngles.y;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
                    RotationSmoothTime);

                if (!_input.aim)
                    // rotate to face input direction relative to camera position
                    transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }

            if (_input.aim)
                targetSpeed *= 0.5f;


            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

            // move the player
            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) +
                             new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

            // update animator if using character
            if (_hasAnimator)
            {
                _animator.SetFloat(_animIDSpeed, _animationBlend);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                // reset the fall timeout timer
                _fallTimeoutDelta = FallTimeout;

                // update animator if using character
                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                // stop our velocity dropping infinitely when grounded
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                // Jump
                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDJump, true);
                    }
                }

                // jump timeout
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                // reset the jump timeout timer
                _jumpTimeoutDelta = JumpTimeout;

                // fall timeout
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else
                {
                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDFreeFall, true);
                    }
                }

                // if we are not grounded, do not jump
                _input.jump = false;
            }

            // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private void Roll()
        {
            bool isRolling = _animator.GetBool(_animIDRollForward);

            if (isRolling)
            {
                _input.roll = false;

                _rollDistanceFraction += Time.deltaTime;
                float t = Mathf.Clamp01(_rollDistanceFraction / _rollDuration);

                // total distance we should have reached by now
                float targetDist = rollCurve.Evaluate(t) * _rollTotalDistance;

                // move only the "remaining" piece for this frame
                float remainingDist = targetDist - _distRolled;
                _distRolled = targetDist;

                Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
                _controller.Move(remainingDist * targetDirection);

                //end of animation
                if (t >= 1f && _hasAnimator)
                {
                    _animator.SetBool(_animIDRollForward, false);

                }
            }




            if (_input.roll && !isRolling)
            {
                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDRollForward, true);
                }

                _rollDistanceFraction = 0f;
                _distRolled = 0f;
                _rollTotalDistance = RollDistance + (_input.sprint ? SprintSpeed : MoveSpeed) / 2;
                // AnimatorStateInfo next = _animator.GetCurrentAnimatorStateInfo(0);
                // if (next.fullPathHash == _animIDRollForward || next.IsTag("Roll"))
                // {
                // state.length already accounts for animation speed
                // _rollDuration = next.length;
                // }
            }
        }

        private void Reload()
        {
            if (_input.reload && !_animator.GetBool(_animIDRollForward)) // can't reload while rolling
            {
                _input.reload = false;
                if (currentAmmo < maxAmmo)
                {
                    // TODO : play reload animation
                    currentAmmo = maxAmmo;
                    ammoDisplay.texture = ammoTextures[currentAmmo];
                }
            }
            else
            {
                return;
            }
        }

        private void Aim()
        {
            crosshair.enabled = _input.aim;
            _lastFireTime += Time.deltaTime;

            if (_input.aim)
            {
                _animator.SetBool("Aim", true);
                // Rotate player to face camera direction
                Vector3 forward = _mainCamera.transform.forward;
                forward.y = 0; // keep flat on ground
                transform.forward = Vector3.Lerp(transform.forward, forward, Time.deltaTime * 10f);
                
                // Count aim duration
                _aimTimer += Time.deltaTime;

                // Shrink crosshair gradually
                _crosshairScale = Mathf.Lerp(_crosshairScale, _minCrosshairScale, Time.deltaTime * _aimShrinkSpeed);
                crosshair.rectTransform.localScale = Vector3.one * _crosshairScale;

                // Handle firing
                if (_input.fire && currentAmmo > 0 && _lastFireTime >= _fireDelay)
                {
                    _lastFireTime = 0f;
                    Fire();
                    ResetCrosshair(); // reset after shot
                }
            }
            else
            {
                _animator.SetBool("Aim", false);
                ResetCrosshair(); // if cancel aim
            }
        }

        private void ResetCrosshair()
        {
            _aimTimer = 0f;
            _crosshairScale = 1.25f;
            crosshair.rectTransform.localScale = Vector3.one * _crosshairScale;
        }

        private void Fire()
        {
            currentAmmo--;
            ammoDisplay.texture = ammoTextures[currentAmmo];

            float inaccuracy = _crosshairScale; // bigger = worse accuracy
            Vector3 shootDir = _mainCamera.transform.forward;
            shootDir.x += Random.Range(-inaccuracy, inaccuracy) * 0.01f;
            shootDir.y += Random.Range(-inaccuracy, inaccuracy) * 0.01f;
            shootDir.z += Random.Range(-inaccuracy, inaccuracy) * 0.01f;

            TrailRenderer tracer = Instantiate(tracerPrefab, muzzlePoint.position, Quaternion.identity);

            if (Physics.Raycast(_mainCamera.transform.position, shootDir, out RaycastHit hit, 100f))
            {
                StartCoroutine(TracerEffect(tracer, hit.point));
                Debug.Log("Hit: " + hit.collider.name);
                // TODO: deal damage to hit target
            }
            else
            {
                Vector3 endPoint = _mainCamera.transform.position + shootDir * 100f;
                StartCoroutine(TracerEffect(tracer, endPoint));
            }

            if (currentAmmo <= 0) Reload();
        }

        private IEnumerator TracerEffect(TrailRenderer tracer, Vector3 hitPoint)
        {
            float time = 0;
            Vector3 startPos = tracer.transform.position;
            while (time < tracerDuration)
            {
                tracer.transform.position = Vector3.Lerp(startPos, hitPoint, time / tracerDuration);
                time += Time.deltaTime;
                yield return null;
            }
            tracer.transform.position = hitPoint;
            Destroy(tracer.gameObject, tracerDuration);
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;

            // when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
            Gizmos.DrawSphere(
                new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z),
                GroundedRadius);
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                if (FootstepAudioClips.Length > 0)
                {
                    var index = Random.Range(0, FootstepAudioClips.Length);
                    AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
                    ZombieAI.OnSoundMade?.Invoke(transform.position, 15f);
                }
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), FootstepAudioVolume);
                ZombieAI.OnSoundMade?.Invoke(transform.position, 25f);
            }
        }
    }
}