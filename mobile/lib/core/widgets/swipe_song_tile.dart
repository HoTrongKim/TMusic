import 'package:flutter/material.dart';

import 'motion.dart';

/// Coordinates swipeable list rows so only one action tray can stay open at a time.
class SwipeActionGroup extends StatefulWidget {
  const SwipeActionGroup({super.key, required this.child});
  final Widget child;

  static ValueNotifier<String?>? maybeOf(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<_SwipeActionScope>()?.notifier;

  static void closeAll(BuildContext context) {
    final scope = context
        .getInheritedWidgetOfExactType<_SwipeActionScope>()
        ?.notifier;
    if (scope != null && scope.value != null) {
      scope.value = null;
    }
  }

  @override
  State<SwipeActionGroup> createState() => _SwipeActionGroupState();
}

class _SwipeActionGroupState extends State<SwipeActionGroup> {
  final _openId = ValueNotifier<String?>(null);

  @override
  void dispose() {
    _openId.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) =>
      _SwipeActionScope(notifier: _openId, child: widget.child);
}

class _SwipeActionScope extends InheritedNotifier<ValueNotifier<String?>> {
  const _SwipeActionScope({required super.notifier, required super.child});
}

class SwipeActionItem {
  const SwipeActionItem({
    this.key,
    required this.icon,
    required this.label,
    required this.backgroundColor,
    this.foregroundColor = Colors.white,
    required this.onPressed,
  });

  final Key? key;
  final IconData icon;
  final String label;
  final Color backgroundColor;
  final Color foregroundColor;
  final VoidCallback onPressed;
}

/// Reusable horizontal left-swipe action container:
/// - Short swipe reveals actions without ever auto-deleting on full swipe.
/// - Yields vertical drags cleanly to parent scroll views.
/// - Ensures only one row is open within a [SwipeActionGroup].
class SwipeActionRow extends StatefulWidget {
  const SwipeActionRow({
    super.key,
    required this.itemId,
    required this.actions,
    required this.child,
    this.enabled = true,
    this.actionWidth = 68,
  });

  final String itemId;
  final List<SwipeActionItem> actions;
  final Widget child;
  final bool enabled;
  final double actionWidth;

  @override
  State<SwipeActionRow> createState() => _SwipeActionRowState();
}

class _SwipeActionRowState extends State<SwipeActionRow>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  ValueNotifier<String?>? _group;
  bool _wasOpen = false;

  double get _maxExtent => widget.actions.length * widget.actionWidth;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 210),
    );
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final nextGroup = SwipeActionGroup.maybeOf(context);
    if (_group != nextGroup) {
      _group?.removeListener(_onGroupChanged);
      _group = nextGroup;
      _group?.addListener(_onGroupChanged);
    }
  }

  @override
  void didUpdateWidget(covariant SwipeActionRow oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!widget.enabled && _controller.value > 0) {
      _close(animate: false);
    }
  }

  @override
  void dispose() {
    _group?.removeListener(_onGroupChanged);
    if (_group?.value == widget.itemId) {
      _group?.value = null;
    }
    _controller.dispose();
    super.dispose();
  }

  void _onGroupChanged() {
    if (_group?.value != widget.itemId && _controller.value > 0) {
      _close();
    }
  }

  void _open() {
    if (!widget.enabled || widget.actions.isEmpty) return;
    if (!_wasOpen) {
      TMusicHaptics.selection();
      _wasOpen = true;
    }
    _group?.value = widget.itemId;
    if (context.reduceMotion) {
      _controller.value = 1.0;
    } else {
      _controller.animateTo(
        1.0,
        duration: const Duration(milliseconds: 210),
        curve: Curves.easeOutCubic,
      );
    }
  }

  void _close({bool animate = true}) {
    _wasOpen = false;
    if (_group?.value == widget.itemId) {
      _group?.value = null;
    }
    if (!animate || !mounted || context.reduceMotion) {
      _controller.value = 0.0;
    } else {
      _controller.animateTo(
        0.0,
        duration: const Duration(milliseconds: 190),
        curve: Curves.easeOutCubic,
      );
    }
  }

  void _onHorizontalDragStart(DragStartDetails details) {
    if (!widget.enabled || widget.actions.isEmpty) return;
    if (_group?.value != null && _group?.value != widget.itemId) {
      _group?.value = null;
    }
  }

  void _onHorizontalDragUpdate(DragUpdateDetails details) {
    if (!widget.enabled || widget.actions.isEmpty || _maxExtent <= 0) return;
    final delta = -details.delta.dx / _maxExtent;
    _controller.value = (_controller.value + delta).clamp(0.0, 1.0);
  }

  void _onHorizontalDragEnd(DragEndDetails details) {
    if (!widget.enabled || widget.actions.isEmpty) return;
    final velocity = details.primaryVelocity ?? 0;
    if (velocity < -260) {
      _open();
    } else if (velocity > 260) {
      _close();
    } else if (_controller.value >= 0.35) {
      _open();
    } else {
      _close();
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.enabled || widget.actions.isEmpty) return widget.child;
    final extent = _maxExtent;
    final bg = Theme.of(context).colorScheme.surfaceContainerLow;

    return ClipRRect(
      borderRadius: BorderRadius.circular(12),
      child: Stack(
        children: [
          Positioned.fill(
            child: AnimatedBuilder(
              animation: _controller,
              builder: (context, child) {
                if (_controller.value <= 0.001) return const SizedBox.shrink();
                return ColoredBox(
                  color: bg,
                  child: Align(
                    alignment: Alignment.centerRight,
                    child: SizedBox(width: extent, child: child),
                  ),
                );
              },
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  for (final action in widget.actions)
                    SizedBox(
                      width: widget.actionWidth,
                      child: Semantics(
                        button: true,
                        label: action.label,
                        child: Material(
                          key: action.key,
                          color: action.backgroundColor,
                          child: InkWell(
                            onTap: () {
                              _close();
                              action.onPressed();
                            },
                            child: Padding(
                              padding: const EdgeInsets.symmetric(horizontal: 4),
                              child: Column(
                                mainAxisAlignment: MainAxisAlignment.center,
                                children: [
                                  Icon(
                                    action.icon,
                                    color: action.foregroundColor,
                                    size: 20,
                                  ),
                                  const SizedBox(height: 3),
                                  Text(
                                    action.label,
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: TextStyle(
                                      color: action.foregroundColor,
                                      fontSize: 10.5,
                                      fontWeight: FontWeight.w700,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
          AnimatedBuilder(
            animation: _controller,
            builder: (context, child) {
              final isOpen = _controller.value > 0.05;
              final anotherOpen =
                  _group?.value != null && _group?.value != widget.itemId;
              return Transform.translate(
                offset: Offset(-_controller.value * extent, 0),
                child: GestureDetector(
                  behavior: HitTestBehavior.translucent,
                  onHorizontalDragStart: _onHorizontalDragStart,
                  onHorizontalDragUpdate: _onHorizontalDragUpdate,
                  onHorizontalDragEnd: _onHorizontalDragEnd,
                  onTap: isOpen
                      ? _close
                      : (anotherOpen ? () => _group?.value = null : null),
                  child: AbsorbPointer(
                    absorbing: _controller.value > 0.1,
                    child: child,
                  ),
                ),
              );
            },
            child: widget.child,
          ),
        ],
      ),
    );
  }
}
