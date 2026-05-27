import { cloneElement, isValidElement, useId, type ReactElement, type ReactNode } from 'react';

type FieldControlProps = {
  id?: string;
  'aria-describedby'?: string;
  'aria-invalid'?: boolean | 'true' | 'false';
};

type FieldProps = {
  label: string;
  children: ReactElement<FieldControlProps>;
  hint?: ReactNode;
  error?: ReactNode;
  className?: string;
};

export function Field({ label, children, hint, error, className }: FieldProps) {
  const generatedId = useId();

  if (!isValidElement<FieldControlProps>(children)) {
    return null;
  }

  const inputId = children.props.id ?? `${generatedId}-control`;
  const hintId = hint ? `${generatedId}-hint` : undefined;
  const errorId = error ? `${generatedId}-error` : undefined;
  const describedBy = [children.props['aria-describedby'], hintId, errorId].filter(Boolean).join(' ') || undefined;
  const control = cloneElement(children, {
    id: inputId,
    'aria-describedby': describedBy,
    'aria-invalid': error ? true : children.props['aria-invalid']
  });

  return (
    <div className={['lrn-field', className].filter(Boolean).join(' ')}>
      <label className="lrn-field__label" htmlFor={inputId}>
        {label}
      </label>
      {control}
      {hint ? (
        <span className="lrn-field__hint" id={hintId}>
          {hint}
        </span>
      ) : null}
      {error ? (
        <span className="lrn-field__error" id={errorId}>
          {error}
        </span>
      ) : null}
    </div>
  );
}
