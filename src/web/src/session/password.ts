/**
 * The shortest password the installation will take (`docs/sign-in.md`), which
 * is a minimum length and nothing else: no composition rules, no rotation, and
 * nothing checked against an outside service.
 *
 * It is here rather than in the one form that first needed it because both
 * screens that set a password have to say the same number, and the one that
 * says a different number is the one that lets a refusal reach the operator
 * after they typed.
 */
export const PASSWORD_MINIMUM = 12;
