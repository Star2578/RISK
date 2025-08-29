using UnityEngine;
using UnityEngine.AI;

public class ZombieAI : EntityBase
{
    public enum State { GoingToVillage, Roaming, Chasing, Attacking }
    private State currentState;

    public NavMeshAgent agent;
    public Animator animator;
    public Transform player;

    [Header("Zombie Settings")]
    public float roamRadius = 10f;
    public float visionRange = 12f;
    public float visionAngle = 60f; // cone half-angle
    public float attackRange = 2f;
    public float chaseSpeed = 3.5f;
    public float roamSpeed = 1.5f;
    public float attackCooldown = 1.5f;

    [Header("Village Area")]
    public Transform villageCenter;   // assigned by EnemiesController
    public Vector2 villageSize = new Vector2(20f, 20f);

    private float lastAttackTime = 0f;
    private Vector3 roamDestination;

    public static System.Action<Vector3, float> OnSoundMade;

    private Vector3 lastHeardSound;
    private bool heardSound = false;
    private float lastHeardRadius;
    private float soundDebugTimer;
    private float soundDebugDuration = 2f;

    void Start()
    {
        currentState = State.GoingToVillage;
        if (player == null)
            player = GameObject.FindGameObjectWithTag("Player").transform;
        agent.speed = roamSpeed;
        PickNewRoamPoint();
    }

    void Update()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        switch (currentState)
        {
            case State.GoingToVillage:
                GoToVillage();
                if (IsInVillage()) SwitchState(State.Roaming);
                break;

            case State.Roaming:
                RoamInsideVillage();
                if (CanSeePlayer()) SwitchState(State.Chasing);
                break;

            case State.Chasing:
                ChasePlayer(distanceToPlayer);
                break;

            case State.Attacking:
                AttackPlayer(distanceToPlayer);
                break;
        }

        if (soundDebugTimer > 0f)
        {
            soundDebugTimer -= Time.deltaTime;
        }
        else
        {
            soundDebugTimer = 0f;
        }
    }

    void GoToVillage()
    {
        agent.speed = roamSpeed;

        if (heardSound)
        {
            agent.SetDestination(lastHeardSound);
            if (!agent.pathPending && agent.remainingDistance < 0.5f)
            {
                heardSound = false; // reached sound
                PickNewRoamPoint(); // resume going inside after checking
            }
        }
        else if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            PickNewRoamPoint(); // go to random point inside the village instead of center
        }

        if (CanSeePlayer())
            SwitchState(State.Chasing);
    }



    bool IsInVillage()
    {
        Vector3 localPos = villageCenter.InverseTransformPoint(transform.position);
        float halfX = villageSize.x * 0.5f;
        float halfZ = villageSize.y * 0.5f;

        return (localPos.x >= -halfX && localPos.x <= halfX &&
                localPos.z >= -halfZ && localPos.z <= halfZ);
    }

    void RoamInsideVillage()
    {
        agent.speed = roamSpeed;

        if (heardSound)
        {
            agent.SetDestination(lastHeardSound);
            if (!agent.pathPending && agent.remainingDistance < 0.5f)
            {
                heardSound = false; // reached sound source
            }
        }
        else if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            PickNewRoamPoint();
        }

        if (CanSeePlayer())
            SwitchState(State.Chasing);
    }


    void PickNewRoamPoint()
    {
        float halfX = villageSize.x * 0.5f;
        float halfZ = villageSize.y * 0.5f;

        Vector3 randomPoint = new Vector3(
            Random.Range(-halfX, halfX),
            0,
            Random.Range(-halfZ, halfZ)
        );

        Vector3 worldPoint = villageCenter.position + randomPoint;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(worldPoint, out hit, 2f, NavMesh.AllAreas))
        {
            roamDestination = hit.position;
            agent.SetDestination(roamDestination);
            animator.SetBool("IsRunning", false);
            animator.SetBool("IsWalking", true);
        }
    }

    void ChasePlayer(float distance)
    {
        Debug.Log("Zombie spotted the player and is chasing!");
        agent.speed = chaseSpeed;
        animator.SetBool("IsRunning", true);
        animator.SetBool("IsWalking", false);
        agent.SetDestination(player.position);

        if (distance <= attackRange)
            SwitchState(State.Attacking);
        else if (!CanSeePlayer()) // lost sight
            SwitchState(State.Roaming);
    }

    void AttackPlayer(float distance)
    {
        agent.ResetPath(); // stop moving

        if (Time.time - lastAttackTime > attackCooldown)
        {
            Debug.Log("Zombie attacks player!");
            // TODO: Damage player
            lastAttackTime = Time.time;
            animator.SetTrigger("Attack");
        }

        if (distance > attackRange)
            SwitchState(State.Chasing);
    }

    void SwitchState(State newState)
    {
        currentState = newState;
    }

    bool CanSeePlayer()
    {
        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, dirToPlayer);

        if (angle < visionAngle && Vector3.Distance(transform.position, player.position) < visionRange)
        {
            // check line of sight
            if (Physics.Raycast(transform.position + Vector3.up * 1.5f, dirToPlayer, out RaycastHit hit, visionRange))
            {
                if (hit.transform == player)
                    return true;
            }
        }
        return false;
    }

    void OnEnable()
    {
        OnSoundMade += HearSound;
    }

    void OnDisable()
    {
        OnSoundMade -= HearSound;
    }

    void HearSound(Vector3 soundPos, float radius)
    {
        if (Vector3.Distance(transform.position, soundPos) <= radius)
        {
            lastHeardSound = soundPos;
            lastHeardRadius = radius;
            heardSound = true;

            // show gizmo for a while
            soundDebugTimer = soundDebugDuration;

            if (currentState == State.Roaming)
            {
                agent.SetDestination(lastHeardSound);
            }
        }
    }

    private void OnDrawGizmos()
    {
        // Village area box
        if (villageCenter != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(villageCenter.position, new Vector3(villageSize.x, 0.1f, villageSize.y));
        }

        if (soundDebugTimer > 0f)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f); // orange, semi-transparent
            Gizmos.DrawWireSphere(lastHeardSound, lastHeardRadius);
        }

        // Vision range circle (XZ plane)
        Gizmos.color = Color.yellow;
        Vector3 pos = transform.position;
        pos.y = 0.05f;
        Gizmos.DrawWireSphere(pos, visionRange);

        // Vision cone
        Gizmos.color = Color.red;
        Vector3 leftBoundary = Quaternion.Euler(0, -visionAngle, 0) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0, visionAngle, 0) * transform.forward;
        Gizmos.DrawRay(transform.position, leftBoundary * visionRange);
        Gizmos.DrawRay(transform.position, rightBoundary * visionRange);
    }

    protected override void Die()
    {
        animator.SetTrigger("Death");
        agent.isStopped = true;
    }
}
