/**
 * The shortest password the installation will take (`docs/sign-in.md`), which
 * is a minimum length and nothing else: no composition rules, no rotation, and
 * nothing checked against an outside service.
 *
 * Sixteen rather than twelve because the second factor is optional (ADR 0041),
 * so this may be the only credential on the account — and length is the one
 * property the product can set (ADR 0042).
 *
 * It is here rather than in the one form that first needed it because both
 * screens that set a password have to say the same number, and the one that
 * says a different number is the one that lets a refusal reach the operator
 * after they typed.
 */
export const PASSWORD_MINIMUM = 16;
