<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Casts\Attribute;
use Illuminate\Database\Eloquent\Model;

// The seeded, read-only todos table (shared/schema.sql): it has created_at but no updated_at.
class Todo extends Model
{
    public $timestamps = false;

    protected $casts = ['due_at' => 'date'];

    protected $hidden = ['due_at'];

    protected $appends = ['due'];

    protected function due(): Attribute
    {
        return Attribute::get(fn () => $this->due_at->format('Y-m-d'));
    }
}
