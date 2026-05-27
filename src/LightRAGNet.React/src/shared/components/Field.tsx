import { cloneElement, isValidElement, useId, type ReactElement, type ReactNode } from 'react';

export type FieldControlProps = {
  id?: string;
  'aria-describedby'?: string;
  'aria-invalid'?: boolean | 'true' | 'false';
};

type FieldProps = {
  label: string;
  children: ReactElement<FieldControlProps> | ((controlProps: FieldControlProps) => ReactNode);
  hint?: ReactNode;
  error?: ReactNode;
  className?: string;
};

export function Field({ label, children, hint, error, className }: FieldProps) {
  const generatedId = useId();
  const elementChild = isValidElement<FieldControlProps>(children) ? children : undefined;
  const inputId = elementChild?.props.id ?? `${generatedId}-control`;
  const hintId = hint ? `${generatedId}-hint` : undefined;
  const errorId = error ? `${generatedId}-error` : undefined;
  const describedBy = [elementChild?.props['aria-describedby'], hintId, errorId].filter(Boolean).join(' ') || undefined;
  const controlProps: FieldControlProps = {
    id: inputId,
    'aria-describedby': describedBy,
    'aria-invalid': error ? true : elementChild?.props['aria-invalid']
  };
  const control = renderControl(children, controlProps);

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

function renderControl(
  children: FieldProps['children'],
  controlProps: FieldControlProps
): ReactNode {
  if (typeof children === 'function') {
    return children(controlProps);
  }

  if (isValidElement<FieldControlProps>(children)) {
    return cloneElement(children, controlProps);
  }

  throw new Error('Field children must be a render function or a valid React element.');
}
