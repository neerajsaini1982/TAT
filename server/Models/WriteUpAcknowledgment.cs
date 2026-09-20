namespace Server.Models;

// Whether the employee has confirmed receiving a write-up. Acknowledging
// means "I received this", not "I agree with it".
public enum WriteUpAcknowledgment
{
    Pending,
    Acknowledged,

    // The employee refused to acknowledge; an admin records that.
    Declined,
}
