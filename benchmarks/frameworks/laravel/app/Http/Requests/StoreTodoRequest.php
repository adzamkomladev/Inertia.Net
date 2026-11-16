<?php

namespace App\Http\Requests;

use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;

// The rules and messages of shared/ts/rules.ts. TrimStrings has already trimmed the title.
class StoreTodoRequest extends FormRequest
{
    public function rules(): array
    {
        return [
            'title' => ['required', 'string', 'between:3,120'],
            'priority' => ['required', 'integer:strict', 'between:0,4'],
            'assignee' => ['required', Rule::in(['ada', 'barbara', 'dennis', 'grace', 'linus'])],
            'due' => ['required', 'date_format:Y-m-d', 'after_or_equal:2026-01-01', 'before_or_equal:2026-12-31'],
        ];
    }

    public function messages(): array
    {
        return [
            'title' => 'Enter a title between 3 and 120 characters.',
            'priority' => 'Choose a priority from 0 to 4.',
            'assignee' => 'Choose someone from the team.',
            'due' => 'Enter a due date in 2026.',
        ];
    }
}
