using NUnit.Framework;
using UnityEngine.InputSystem;

namespace UnityTemplate.Tests.EditMode
{
    public class InputSystemIntegrationTests : InputTestFixture
    {
        [Test]
        public void BoundActionPerformsAndCancelsWithVirtualKeyboard()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            using (var action = new InputAction("Confirm", InputActionType.Button))
            {
                action.AddBinding(keyboard.spaceKey);
                var performed = 0;
                var canceled = 0;
                action.performed += _ => performed++;
                action.canceled += _ => canceled++;
                action.Enable();

                Press(keyboard.spaceKey);

                Assert.That(action.IsPressed(), Is.True);
                Assert.That(performed, Is.EqualTo(1));

                Release(keyboard.spaceKey);

                Assert.That(action.IsPressed(), Is.False);
                Assert.That(canceled, Is.EqualTo(1));
            }
        }
    }
}
