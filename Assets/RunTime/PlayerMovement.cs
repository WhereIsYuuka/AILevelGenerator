using UnityEngine;
using UnityEngine.InputSystem;

namespace AILevelGenerator.Runtime
{
    /// <summary>
    /// 角色控制器：WASD 水平移动，速度为 moveSpeed（默认 5），带简单重力。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        public float moveSpeed = 5f;

        private CharacterController controller;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            // 轮询键盘（新 Input System 不支持 Input.GetAxisRaw）
            Vector2 input = Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) input.y += 1f;
                if (keyboard.sKey.isPressed) input.y -= 1f;
                if (keyboard.aKey.isPressed) input.x -= 1f;
                if (keyboard.dKey.isPressed) input.x += 1f;
            }

            Vector3 move = transform.right * input.x + transform.forward * input.y;
            if (move.sqrMagnitude > 1f) move.Normalize(); // 斜向移动不超速

            Vector3 velocity = move * moveSpeed + Vector3.down * 9.81f; // 简单重力
            controller.Move(velocity * Time.deltaTime);
        }
    }
}
