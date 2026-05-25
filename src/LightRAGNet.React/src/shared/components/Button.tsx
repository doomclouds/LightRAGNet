import type { AnchorHTMLAttributes, ButtonHTMLAttributes, ReactNode } from 'react';

type ButtonTone = 'primary' | 'secondary' | 'danger';

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  tone?: ButtonTone;
  children: ReactNode;
};

type ButtonLinkProps = AnchorHTMLAttributes<HTMLAnchorElement> & {
  tone?: ButtonTone;
  children: ReactNode;
};

export function Button({ tone = 'secondary', className, type = 'button', ...props }: ButtonProps) {
  return (
    <button
      {...props}
      type={type}
      className={getButtonClassName(tone, className)}
    />
  );
}

export function ButtonLink({ tone = 'secondary', className, ...props }: ButtonLinkProps) {
  return (
    <a
      {...props}
      className={getButtonClassName(tone, className)}
    />
  );
}

function getButtonClassName(tone: ButtonTone, className?: string) {
  return ['lrn-button', `lrn-button--${tone}`, className].filter(Boolean).join(' ');
}
