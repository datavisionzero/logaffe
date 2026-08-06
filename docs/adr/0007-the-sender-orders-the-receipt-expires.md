# The Sender Orders, the Receipt Expires

An entry carries two timestamps: the event time the sender put in `@t`, and the
time logaffe received the batch. The UI orders by the sender's, because that is
the order in which things actually happened and it survives a delivery that was
queued behind a slow network. Retention counts from logaffe's, because a machine
with a wrong clock is an ordinary occurrence and a single field would let it
poison both jobs at once — an entry dated next year would never be cleaned up,
and one dated last year would be swept away on arrival.

## Consequences

Both timestamps are stored, and the retention sweep therefore runs on a value no
sender can influence, which is also what keeps a misconfigured application from
filling the store past its window. The two can disagree visibly: an application
that was disconnected for an hour produces rows whose event times are old while
their retention runs from now. That is the correct reading in both directions —
they happened then, and logaffe has known about them since now.
