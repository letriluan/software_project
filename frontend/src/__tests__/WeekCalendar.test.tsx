import { render, screen, fireEvent } from '@testing-library/react';
import { WeekCalendar } from '../components/WeekCalendar';
import { weekDays, weekStart } from '../lib/time';
import { lesson } from '../test/server';

const days = weekDays(weekStart(new Date('2026-11-10T00:00:00Z')));

describe('WeekCalendar', () => {
  it('places a 4pm one-hour lesson 10 hours below the 6am top line', () => {
    render(<WeekCalendar days={days} lessons={[lesson()]} now={new Date('2026-11-09T00:00:00Z')}
      onSelect={() => {}} onSlot={() => {}} />);
    const block = screen.getByRole('button', { name: /Ava Nguyen, Mathematics, 4:00pm/ });
    expect(block.style.top).toBe(`${10 * 56 + 1}px`);
    expect(block.style.height).toBe(`${56 - 3}px`);
    expect(block).not.toHaveClass('needs-marking');
  });

  it('highlights a started lesson that still needs attendance', () => {
    render(<WeekCalendar days={days} lessons={[lesson()]} now={new Date('2026-11-10T07:00:00Z')}
      onSelect={() => {}} onSlot={() => {}} />);
    expect(screen.getByRole('button', { name: /needs attendance/ })).toHaveClass('needs-marking');
  });

  it('calls onSelect with the lesson', () => {
    const onSelect = vi.fn();
    render(<WeekCalendar days={days} lessons={[lesson()]} now={new Date('2026-11-09T00:00:00Z')}
      onSelect={onSelect} onSlot={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: /Ava Nguyen/ }));
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ id: 'l1' }));
  });

  it('turns a click on an empty slot into a half-hour start time', () => {
    const onSlot = vi.fn();
    render(<WeekCalendar days={days} lessons={[]} now={new Date('2026-11-09T00:00:00Z')}
      onSelect={() => {}} onSlot={onSlot} />);
    const wednesday = screen.getByRole('gridcell', { name: 'Wednesday 11 November' });
    wednesday.getBoundingClientRect = () => ({ top: 100 }) as DOMRect;
    fireEvent.click(wednesday, { clientY: 100 + 56 * 3 + 40 }); // 3h40m below 6am -> 9:30
    expect(onSlot).toHaveBeenCalledWith('2026-11-11', '09:30');
  });

  it('does not offer booking on past days', () => {
    const onSlot = vi.fn();
    render(<WeekCalendar days={days} lessons={[]} now={new Date('2026-11-12T00:00:00Z')}
      onSelect={() => {}} onSlot={onSlot} />);
    fireEvent.click(screen.getByRole('gridcell', { name: 'Monday 9 November' }), { clientY: 200 });
    expect(onSlot).not.toHaveBeenCalled();
  });
});
